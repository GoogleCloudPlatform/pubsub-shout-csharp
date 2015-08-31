var system = require('system');

var url = ('https://' + system.env['GOOGLE_APP_VERSION'] + '-dot-'
    + system.env['GOOGLE_APP_ID'] + '.appspot.com/');

var page = require('webpage').create();
page.onResourceRequested = function(request) {
  console.log('Request ' + JSON.stringify(request, undefined, 4));
};
page.onResourceReceived = function(response) {
  console.log('Receive ' + JSON.stringify(response, undefined, 4));
};
page.open(url);
setTimeout(function() {
  console.log('Timeout.');
  phantom.exit(1);
}, 5000);
