## Windows Shout Example

See [Invoking Legacy Code on Google Cloud Platform](
https://cloud.google.com/solutions/invoking-legacy-code).

A C# application designed to run on Google Compute Engine.
It demonstrates how to use
[Google Cloud Pub/Sub](https://cloud.google.com/pubsub/docs).

## Links
 - [Invoking Legacy Code on Google Cloud Platform](
   https://cloud.google.com/solutions/invoking-legacy-code).
 - [.NET on Google Cloud Platform](https://cloud.google.com/dotnet/)
 - [Google Cloud Pub/Sub API Client Library for .NET](
   https://developers.google.com/api-client-library/dotnet/apis/pubsub/v1)

## Build
0.  On your Windows development machine,
1.  In the [Google Developers Console](https://console.developers.google.com/),
    select the project you created running the code in
    [appengine-python-flask](../appengine-python-flask).
2.  In the Google Developers Console, on the left-hand side, click
    "APIs & auth", then click Credentials.  Click the button to "Generate
    a new JSON key."  Set the environment variable
    `GOOGLE_APPLICATION_CREDENTIALS` to the path of the JSON key you
    downloaded.
3.  Clone this repo with

    ```sh
    git clone https://github.com/GoogleCloudPlatform/pubsub-shout-csharp
    ```
4.  Open ShoutService.sln with Microsoft Visual Studio version 2012 or later.
5.  Edit `ShoutLib/Constants.cs`.  Update the variable `ProjectId`.
6.  Build the Solution.
7.  Run it locally by pressing F5 or choosing "Debug -> Start Debugging" from
    Microsoft Visual Studio's Menu.

## Deploy
To deploy the application:

1.  Use the Google Developers Console to create a new VM instance with a
    Windows Server 2012 boot disk image.  *Before* clicking the `Create`
    button, click "Management, disk, networking, access & security options"
    and under "Access & security", change `Cloud Platform` to  `Enabled`.
    Now click 'Create.'
2.  Wait for the instance to be created, then connect to it via Remote
    Desktop.
3.  Copy the contents of your Release folder from your development machine
    to the VM instance.
4.  Using Windows explorer, give the user `Local Service` permission to
    read and execute the Release directory.
5.  Install the service with installutil.exe:

    ```sh
    C:\Users\shout7q\Desktop\Release>\Windows\Microsoft.NET\Framework\v4.0.30319\InstallUtil.exe ShoutService.exe
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
