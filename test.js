// Copyright 2015 Google Inc. All Rights Reserved.
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//      http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

// Run with phantomjs.

// A timeout for this whole script.
setTimeout(function () {
    console.log('Timeout.');
    phantom.exit(1);
}, 30000);

var system = require('system');

var url = ('https://' + system.env['GOOGLE_APP_VERSION'] + '-dot-'
+ system.env['GOOGLE_APP_ID'] + '.appspot.com/');

var page = require('webpage').create();

var TestCase = function TestCase(input, expectedOutput, expectedStatus) {
    this.input = input;
    this.expectedOutput = expectedOutput;
    this.expectedStatus = expectedStatus;
};

var tests = [
    new TestCase('hi', 'HI', 'Done!'),
    new TestCase('chickens', 'chickens', 'Error.')
];

// An index into the test cases above.
var test = 0;

function startTest() {
    // Fill the text box with "hi" and press the "Submit" button.
    page.evaluate(function (inputValue) {
        var textInput = document.getElementById('shoutText');
        textInput.value = inputValue;
        postForm();
    }, tests[test].input);
}

function onShoutStatus() {
    // Compare the results to our expectations.
    var text = page.evaluate(function () {
        return document.getElementById('shoutText').value;
    });
    var status = page.evaluate(function () {
        return document.getElementById('status').innerText;
    });
    if (text == tests[test].expectedOutput &&
        0 == status.indexOf(tests[test].expectedStatus)) {
        // This test succeeded.
        test += 1;
        if (test >= tests.length) {
            // Finished all the tests.
            console.log('Success.');
            phantom.exit(0);
        } else {
            startTest();  // Start the next test.
        }
    }
}

page.onResourceRequested = function (request) {
    console.log('Request ' + JSON.stringify(request, undefined, 4));
};

page.onResourceReceived = function (response) {
    console.log('Receive ' + JSON.stringify(response, undefined, 4));
    if (response.stage == 'end' && response.url.indexOf('/connect') > -1) {
        setTimeout(startTest, 1);
    }

    if (response.stage == 'end' && response.url.indexOf('/shout') > -1) {
        setTimeout(onShoutStatus, 1);
    }
};

// Fetch the home page.
page.open(url);
