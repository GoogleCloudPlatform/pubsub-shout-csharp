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

from google.appengine.ext import ndb


class Rotoken(object):
    """A rotating token manager.

    Keeps a list of security tokens in datastore.  Periodically,
    a token is added to the list and an old one is dropped (rotated.)
    """

    def __init__(self, datastore_key='SINGLETON', max_tokens=3):
        """Creates a rotating token manager.

        Args:
          datastore_key: the key that will be used to store the list.
          max_tokens: how many tokens should we keep around?
        """
        self._key = ndb.Key(RotokenRecord, datastore_key)
        self._max_tokens = max_tokens

    @ndb.transactional
    def init(self, first_token):
        """Creates the first token in the datastore.

        Should be called exactly once after the app has been first uploaded.
        Args:
          first_token: string, the token to insert into the list.
        """
        record = self._key.get()
        if not record:
            record = RotokenRecord(key=self._key, tokens=[first_token])
        record.put()

    @ndb.transactional
    def rotate_token(self, token):
        """Rotates in a new token and drops an old token."""
        record = self._key.get()
        record.tokens.insert(0, token)
        del record.tokens[self._max_tokens:]
        record.put()

    def get_tokens(self):
        """Returns the list of tokens.

        Because this is a key.get() and not a query, and because the value
        rarely changes, most of the time this call will be satisfied from
        memcache.
        """
        return self._key.get().tokens


class RotokenRecord(ndb.Model):
    tokens = ndb.StringProperty(indexed=False, repeated=True)
