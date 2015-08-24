## Windows Shout Example

A C# application designed to run on Google Compute Engine.
It demonstrates how to use
[Google Cloud Pub/Sub](https://cloud.google.com/pubsub/docs).

## Build
1. Clone this repo with
   ```sh
   git clone https://github.com/GoogleCloudPlatform/pubsub-shout-csharp
   ```
2. Open ShoutSelvice.sln with Microsoft Visual Studio version 2012 or later.
3. Edit `ShoutLib/Constants.cs`.  Update the variables
   `ProjectId`, `ServiceAccountEmail`, and `ServiceAccountP12KeyPath`.
4. Build the Solution.
5. Run it locally by pressing F5 or choosing "Debug -> Start Debugging" from
   Microsoft Visual Studio's Menu.

## Deploy
To deploy the application:

1. If you haven't already,
   use the [Google Developers Console](https://console.developers.google.com/)
   to create a new project id.
2. Use the Google Developers Console to create a new VM instance with a
   Windows Server 2012 boot disk image.  *Before* clicking the `Create`
   button, click "Management, disk, networking, access & security options"
   and under "Access & security", change `Cloud Platform` to  `Enabled`.
   Now click 'Create.'
3. Wait for the instance to be created, then connect to it via Remote
   Desktop.
4. Copy the contents of your Release folder from your development machine
   to the VM instance.
5. Using Windows explorer, give the user `Local Service` permission to
   read and execute the Release directory.
6. Install the service with installutil.exe:

   ```sh
   C:\\Users\\shout7q\\Desktop\\Release>\\Windows\\Microsoft.NET\\Framework\\v4.0.30319\\InstallUtil.exe ShoutService.exe
   ```
## Next Steps
Read the readme in the appengine-python-flask directory.

### Feedback
Star this repo if you found it useful. Use the github issue tracker to give
feedback on this repo.

## Licensing
See [LICENSE](LICENSE)

## Author
Jeffrey Rennie
