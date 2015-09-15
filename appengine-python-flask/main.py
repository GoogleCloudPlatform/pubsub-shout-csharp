# Copyright 2015 Google Inc. All Rights Reserved.
#
# Licensed under the Apache License, Version 2.0 (the "License");
# you may not use this file except in compliance with the License.
# You may obtain a copy of the License at
#
#      http://www.apache.org/licenses/LICENSE-2.0
#
# Unless required by applicable law or agreed to in writing, software
# distributed under the License is distributed on an "AS IS" BASIS,
# WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
# See the License for the specific language governing permissions and
# limitations under the License.

"""
A shout server with streaming status updates.

Streaming is accomplished by long HTTP requests from the browser that block
until there is some change in status.

This can't be a pure REST API because we assume the user's data is sensitive,
and we want to keep it private.  Therefore, we never want the data to be
retrievable by examining HTTP logs.  Therefore, sensitive data are always sent
from the browser via HTTP POST payload.

However, this design still follows an important REST principal: each response
includes a menu of actions that can be performed on the result.  They are
represented as JSON objects with members:
  target:  a url
  method:  POST, GET, PUT, etc.
  token:  a token that must be sent in the payload.
This means it will be easy to implement new clients besides the javascript
client contained in form.html, and that major changes to the server will not
require changes in clients.
"""
import json
import os
import random
import string
import time
import datetime
import pubsub
import traceback
import werkzeug.urls
import socket
import rotoken

import flask
from flask import Flask, request
import jinja2

from google.appengine.api import app_identity
from google.appengine.api import modules
from google.appengine.ext import ndb

app = Flask(__name__)
app.config['DEBUG'] = True
# Where my rotating tokens are kept.
purse = rotoken.Rotoken(modules.get_current_version_name())

JINJA_ENVIRONMENT = jinja2.Environment(
    loader=jinja2.FileSystemLoader(os.path.join(os.path.dirname(__file__),
                                                'templates')),
    extensions=['jinja2.ext.autoescape'],
    autoescape=True)

TOPIC = "shout-requests"
SUBSCRIPTION = "shout-request-workers"
TIMEOUT_SECONDS = 90
APP_ID = app_identity.get_application_id()
RANDOM_ID_LEN = 43  # Equivalent to 256 bits of randomness.

###############################################################################
# Data model.
STATUSES = ('a-new', 'b-shouting', 'c-error', 'd-fatal', 'e-success')
STATUS_MAP = dict((status.split('-')[1], status) for status in STATUSES)
ID_CHARS = string.ascii_letters + string.digits
assert pow(len(ID_CHARS), RANDOM_ID_LEN) > pow(2, 256)


def new_random_id(id_len=RANDOM_ID_LEN):
    """Returns a new random id string.

    We need an id that cannot be guessed by an attacker.  So generate one
    using a secure random number generator.
    """
    rand = random.SystemRandom()
    return ''.join([rand.choice(ID_CHARS) for i in range(id_len)])


class ShoutStatusLog(ndb.Model):
    """Represents the status of a shout request.

    This status log is designed for scalability.  If you are used to SQL,
    it will seem strange.  It's scalable because no transactions are
    necessary, no writes block any other reads or writes, and no read or write
    touches more than one entity.
    It's also designed so that when something goes wrong, the problem will be
    easy to debug by looking at the history in the status log.

    Properties:

    combined_shout_id:  A concatenated string of
      browser_id + '-' + shout_id.  See join_ids().
      browser_id is secret and unique to the browser.
      shout_id is not secret, and is provided by the browser.

    status:
      Legal statuses are:
      a-new
      b-shouting
      c-error
      d-fatal
      e-success
    Note how the status codes (a, b, c, d, e) rank the statuses by priority.
    In other words, I can query the status log in descending order and see the
    highest priority status first.  So I only need to examine one entity to see
    the current status.

    timestamp:  The timestamp of when the entity was inserted.

    error:  When the status is 'c-error' or 'd-fatal', contains an error
            message.

    result:  When the status is 'e-success', contains the shouted string.

    host:  The name of the machine that reported this status.
    """
    combined_shout_id = ndb.StringProperty(name='shout_id', required=True)
    status = ndb.StringProperty(choices=STATUSES)
    timestamp = ndb.DateTimeProperty(auto_now_add=True)
    error = ndb.StringProperty()
    result = ndb.StringProperty(indexed=False)
    host = ndb.StringProperty()  # For debugging purposes only.

    @property
    def status_name(self):
        """Strips the status code and returns the status name."""
        return self.status.split('-')[1] if self.status else ''


###############################################################################
# HTTP handlers.

@app.route('/')
def home():
    """Returns the form containing the javascript client."""
    return flask.render_template('form.html', world='Earth.',
                                 timeout_seconds=TIMEOUT_SECONDS)


@app.route('/connect', methods=['POST'])
def connect():
    """Returns instructions to clients on how to submit shout requests."""
    token = werkzeug.urls.url_encode({
        'browserId': new_random_id()
    })
    return json.dumps({
        # Tell the client how to send shout requests to us.
        'shoutLink': {
            'target': 'shout',
            'method': 'POST',
            'token': token,
        },
    })


@app.route('/shout', methods=['POST'])
def shout():
    """Creates a new shout request.  Returns status of the pending request."""
    token = werkzeug.urls.url_decode(request.form['token'])
    # Insert a status log entity into data store.
    entity = ShoutStatusLog()
    entity.combined_shout_id = combine_ids(token['browserId'],
                                           request.form['shoutId'])
    entity.status = STATUS_MAP['new']
    entity.host = socket.gethostname()
    async_put = entity.put_async()

    # Publish a shout request message to the Pub/Sub topic.
    deadline = utctimestamp() + TIMEOUT_SECONDS
    ps = pubsub.PubSub(APP_ID)
    query = werkzeug.urls.url_encode({
        'browserId': token['browserId'],
        'shoutId': request.form['shoutId'],
    })
    ps.publish(TOPIC, request.form['text'], {
        'deadline': str(deadline),
        'postStatusUrl': 'https://%s/post_shout_status?%s' %
                         (socket.getfqdn(socket.gethostname()), query),
        'postStatusToken': purse.get_tokens()[0],
    })
    async_put.get_result()
    # Wait for a result.
    return poll_shout_status(token['browserId'], request.form['shoutId'], 'new')


@app.route('/shout_status', methods=['POST'])
def shout_status():
    """Check on the status of a pending shout request."""
    token = werkzeug.urls.url_decode(request.form['token'])
    return poll_shout_status(token['browserId'], token['shoutId'],
                             token['status'])


def poll_shout_status(browser_id, shout_id, last_status):
    """Poll datastore waiting for the shout request to complete.

    Why not return the status immediately?
    That would work too, but then the browser would be constantly sending new
    HTTP requests to check to see if its shout request is done.  That would
    consume the users' bandwidth and battery life.

    Why not poll until the shout request is complete?  Why timeout after 45
    seconds?
    Because App Engine will terminate any HTTP request that doesn't complete
    in 60 seconds.  We have to return something before that deadline.

    Args:
        name: string, the name of the request.
        last_status: string, the last status observed by the user.
        deadline: datetime.datetime, when this request should time out.
    Returns:
        A flask http response.
    """
    response = {'shoutId': shout_id, 'status': last_status}
    start_timestamp = time.time()
    ndb.get_context().set_cache_policy(False)
    sleep_seconds = 0.1
    while True:
        # Look up the current status in datastore.
        q = (ShoutStatusLog.query()
             .filter(ShoutStatusLog.combined_shout_id == combine_ids(
                     browser_id, shout_id))
             .order(-ShoutStatusLog.status))
        entities = q.fetch(1)
        if entities:
            entity = entities[0]
            status = response['status'] = entity.status_name
            if status == 'success':
                response['result'] = entity.result
                return json.dumps(response)
            if status == 'fatal':
                response['error'] = entity.error
                return json.dumps(response)
            if last_status != status:
                # State changed, notify user.
                response['error'] = entity.error
                break

        # Retry with exponential backoff, for a maximum of 45 seconds.
        if time.time() - start_timestamp >= 45:
            break
        else:
            time.sleep(sleep_seconds)  # Retry after small wait.
            sleep_seconds = min(5, sleep_seconds * 2)

    response['nextLink'] = {
        'target': 'shout_status',
        'method': 'POST',
        'token': werkzeug.urls.url_encode({
            'browserId': browser_id,
            'shoutId': shout_id,
            'status': response['status']
        })}
    return json.dumps(response), 202


@app.route('/post_shout_status', methods=['POST'])
def post_shout_status():
    """Stores the shout status in datastore.

    Called by our Windows worker processes.
    """
    if request.form.get('token') not in purse.get_tokens():
        flask.abort(403)
    entity = ShoutStatusLog()
    entity.combined_shout_id = combine_ids(request.args['browserId'],
                                           request.args['shoutId'])
    entity.status = STATUS_MAP[request.form['status']]
    if request.form['status'] in ('error', 'fatal'):
        entity.error = request.form.get('result')
    else:
        entity.result = request.form.get('result')
    entity.host = request.form.get('host')
    entity.put()
    return '{}'


def combine_ids(browser_id, shout_id):
    return '%s-%s' % (browser_id, shout_id)


@app.route('/purge')
def purge():
    """Removes old entries from the datastore."""
    too_old = datetime.datetime.utcnow() - datetime.timedelta(days=1)
    q = ShoutStatusLog.query(ShoutStatusLog.timestamp < too_old)
    keys = list(q.iter(keys_only=True))
    for key in keys:
        key.delete()
    return "purged."


@app.route('/rotate_token')
def rotate_token():
    """Rotates the security token used to authenticate post_shout_status
    requests.
    """
    purse.rotate_token(new_random_id())
    return "ok."


@app.route('/init')
def init():
    """Called once by an admin to create pubsub topics and subscriptions."""
    ps = pubsub.PubSub(APP_ID)
    errors = []
    for lam in (
            lambda: ps.create_topic(TOPIC),
            lambda: ps.subscribe(TOPIC, SUBSCRIPTION),
            lambda: purse.init(new_random_id())):
        try:
            lam()
        except:
            errors.append(traceback.format_exc())
    if errors:
        return "\n".join(errors), 500
    return "ok"


def utctimestamp():
    """Returns seconds since the epoch in utc time."""
    return long(time.mktime(time.gmtime()))
