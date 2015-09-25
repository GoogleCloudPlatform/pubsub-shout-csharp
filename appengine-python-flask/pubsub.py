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

import httplib2
import httplib
import base64
import socket
import threading
import discovery_doc
from apiclient import discovery
from oauth2client import client as oauth2client


PUBSUB_SCOPES = ['https://www.googleapis.com/auth/pubsub']


def create_pubsub_client(disdoc, http=None):
    """Create an authenticated pubsub client.

    See https://cloud.google.com/pubsub/configure
    """
    credentials = oauth2client.GoogleCredentials.get_application_default()
    if credentials.create_scoped_required():
        credentials = credentials.create_scoped(PUBSUB_SCOPES)
    if not http:
        http = httplib2.Http(timeout=45)
    credentials.authorize(http)
    return discovery.build_from_document(disdoc, http=http)


class PubSub(object):
    """A thin wrapper around the REST pubsub API.  Thread-safe and cheap.

    Takes care of threading issues and authentication.
    """

    _thread_local = threading.local()
    _discovery_doc = discovery_doc.retrieve_discovery_doc('pubsub', 'v1')

    def __init__(self, project_name):
        self.project_name = project_name

    def _client(self):
        client = getattr(self._thread_local, 'client', None)
        if not client:
            client = self._thread_local.client = create_pubsub_client(
                self._discovery_doc)
        return client

    def create_topic(self, new_unique_name):
        self._client().projects().topics().create(
            name=self._make_topic_path(new_unique_name), body={}).execute(
            num_retries=3)

    def publish(self, topic, data, attributes):
        body = {'messages': [{'data': base64.b64encode(data),
                              'attributes': attributes}]}
        self._client().projects().topics().publish(
            topic=self._make_topic_path(topic), body=body).execute(
            num_retries=3)

    def _make_subscription_path(self, subscription):
        return 'projects/%s/subscriptions/%s' % (self.project_name,
                                                 subscription)

    def _make_topic_path(self, topic):
        return 'projects/%s/topics/%s' % (self.project_name, topic)

    def subscribe(self, topic, new_unique_name, push_config=None):
        body = {'topic': self._make_topic_path(topic), 'ackDeadlineSeconds': 15,
                'pushConfig': push_config}
        self._client().projects().subscriptions().create(
            name=self._make_subscription_path(new_unique_name),
            body=body).execute(num_retries=3)

    def pull(self, subscription, max_messages):
        body = {
            'returnImmediately': False,
            'maxMessages': max_messages,
        }
        try:
            resp = self._client().projects().subscriptions().pull(
                subscription=self._make_subscription_path(subscription),
                body=body).execute(num_retries=3)
        except socket.timeout:
            return []
        except httplib.HTTPException:
            return []
        messages = resp.get('receivedMessages')
        for message in messages:
            data = base64.b64decode(message['message'].get('data', ''))
            message['message']['data'] = data
        return messages

    def acknowledge(self, subscription, ack_id):
        body = {'ackIds': [ack_id]}
        self._client().projects().subscriptions().acknowledge(
            subscription=self._make_subscription_path(subscription),
            body=body).execute(num_retries=3)

    def delete_subscription(self, subscription):
        self._client().projects().subscriptions().delete(
            subscription=self._make_subscription_path(subscription)
        ).execute(num_retries=3)

    def delete_topic(self, topic):
        self._client().projects().topics().delete(
            topic=self._make_topic_path(topic)).execute(num_retries=3)
