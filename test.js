// Run with phantomjs.

// Test timeout.
setTimeout(function() {
  console.log('Timeout.');
  phantom.exit(1);
}, 30000);

var system = require('system');

var url = ('https://' + system.env['GOOGLE_APP_VERSION'] + '-dot-'
    + system.env['GOOGLE_APP_ID'] + '.appspot.com/');

var page = require('webpage').create();

page.onResourceRequested = function(request) {
  console.log('Request ' + JSON.stringify(request, undefined, 4));
};

function onConnect() {
  // Fill the text box with "hi" and press the "Submit" button.
  page.evaluate(function() {
    var textInput = document.getElementById('shoutText');
    textInput.value = "hi";
    postForm();
  });
}

function onShoutStatus() {
  // Fill the text box with "hi" and press the "Submit" button.
  var text = page.evaluate(function() {
    return document.getElementById('shoutText').value;
  });
  if (text == "HI") {
    console.log('Success.');
    phantom.exit(0);
  }
}

page.onResourceReceived = function(response) {
  console.log('Receive ' + JSON.stringify(response, undefined, 4));
  if (response.stage == 'end' && response.url.indexOf('/connect') > -1)
  {
    setTimeout(onConnect, 1);
  }

  if (response.stage == 'end' && response.url.indexOf('/shout') > -1)
  {
    setTimeout(onShoutStatus, 1);
  }
};

// Fetch the home page.
page.open(url);
