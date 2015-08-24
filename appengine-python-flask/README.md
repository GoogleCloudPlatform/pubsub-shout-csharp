## Shout Example for Google App Engine

A Python application on Google App Engine with the
[Flask micro framework](http://flask.pocoo.org).  It demonstrates how to use
[Google Cloud Pub/Sub](https://cloud.google.com/pubsub/docs) to invoke a
Windows backend.

## Build
1. Install the [App Engine Python SDK](https://developers.google.com/appengine/downloads).
See the README file for directions. You'll need
[python 2.7](https://www.python.org/download/releases/2.7/) and
[pip 1.4 or later](http://www.pip-installer.org/en/latest/installing.html) installed too.

2. Clone this repo with

   ```sh
   git clone https://github.com/GoogleCloudPlatform/pubsub-shout-csharp
   ```
3. Install dependencies in the project's lib directory.
   Note: App Engine can only import libraries from inside your project directory.

   ```sh
   cd appengine-python-flask
   pip install -r requirements.txt -t lib
   ```

## Deploy
To deploy the application:

1. Use the [Google Developers Console](https://console.developers.google.com/)
   to create a new project id.
2. [Deploy the
   application](https://developers.google.com/appengine/docs/python/tools/uploadinganapp) with

   ```
   appcfg.py -A <your-project-id> --oauth2 update .
   ```
3. Initialize the app by visiting https://&lt;your-project-id&gt;.appspot.com/init.
   This creates the work queue.  You only need visit this page once, ever.
   Repeatedly visiting this page will print an error because the resources it
   creates have already been created.
4. Congratulations!  Your application is now live at your-project-id.appspot.com
   Try entering some text and clicking "Submit."  The status will change to
   Queueing...  After 90 seconds, the request will timeout, because we haven't
   built the backends yet!

## Next Steps
Read the readme in the windows-csharp directory.

### Feedback
Star this repo if you found it useful. Use the github issue tracker to give
feedback on this repo.

## Licensing
See [LICENSE](LICENSE)

## Author
Jeffrey Rennie
