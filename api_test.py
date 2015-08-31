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
"""Tests the python app engine code.  Tests the JSON API.
TODO: Automatically deploy new Windows backends when the csharp code
      changes.
"""
import json
import os
import pprint
import urllib
import urllib2
import urlparse

HOST = 'https://{0}-dot-{1}.appspot.com'.format(
    os.environ['GOOGLE_APP_VERSION'], os.environ['GOOGLE_APP_ID'])


class Failure(Exception):
    def __init__(self, status, errors):
        self.status = status
        self.errors = errors

    def __str__(self):
        return "Failed with status {}.  Errors:\n{}".format(
            self.status, "\n".join(self.errors))


class BrowserState(object):
    """Behaves like the browser.  Invokes our JSON API."""
    def __init__(self, host):
        """host should include the scheme; ex: https://www.google.com"""
        self.shout_id = 0
        self.final_link = None
        self.shout_link = None
        self.host = host
        self.urlopen("{}/connect".format(host), '')

    def __enter__(self):
        return self

    def __exit__(self, exc_type, exc_val, exc_tb):
        """Invokes final_link if we saw one."""
        if self.final_link:
            self.urlopen(self.final_link['target'], {
                'token': self.final_link['token']})

    def handle_response(self, response):
        """Expects a successful HTTP response.

        :arg response: a return value from urllib2.urlopen()
        :returns a decoded json dict.
        """
        assert response.getcode() >= 200
        assert response.getcode() < 300
        reply = json.load(response)
        pprint.pprint(reply)
        self.shout_link = reply.get('shoutLink', self.shout_link)
        self.final_link = reply.get('finalLink', self.final_link)
        return reply

    def urlopen(self, target, keyval):
        """Call urllib2.urlopen() for the target link.

        :arg target: a url or partial url relative to host name.
        :returns a decoded json dict.
        """
        url = urlparse.urljoin(self.host, target)
        print url
        pprint.pprint(keyval)
        response = urllib2.urlopen(url, urllib.urlencode(keyval))
        return self.handle_response(response)

    def shout(self, text):
        """Invokes shout on the text."""
        shout_id = self.shout_id
        self.shout_id += 1
        payload = {
            'token': self.shout_link['token'],
            'text': text,
            'shoutId': shout_id
        }
        result = None
        errors = []
        reply = self.urlopen(self.shout_link['target'], payload)
        while True:
            result = reply.get('result', result)
            if reply.get('error'):
                errors.append(reply['error'])
            if 'nextLink' in reply:
                next_link = reply['nextLink']
                payload = {
                    'token': next_link['token'],
                    'shoutId': shout_id,
                }
                reply = self.urlopen(next_link['target'], payload)
            else:
                break
        if reply['status'] == 'success':
            return result
        else:
            raise Failure(reply['status'], errors)


with BrowserState(HOST) as state:
    assert 'HELLO' == state.shout('hello')
    assert 'JEFF' == state.shout('jeff')
    try:
        state.shout('chickens')
        assert False
    except Failure:
        pass

print "ok"
