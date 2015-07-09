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
using Google.Apis.Auth.OAuth2;
using Google.Apis.Http;

// Found in C:\Program Files (x86)\Reference Assemblies\Microsoft\WMI\v1.0\Microsoft.Management.Infrastructure.dll
using Microsoft.Management.Infrastructure;
using Microsoft.Practices.EnterpriseLibrary.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;

namespace ShoutLib
{
    public class Auth
    {
        public class Initializer
        {
            /// <summary>
            /// The email address copied and pasted from Google Developer Console's Auth page.
            /// </summary>
            public string ServiceAccountEmail = Constants.ServiceAccountEmail;

            public IEnumerable<string> scopes =
                new string[] { @"https://www.googleapis.com/auth/cloud-platform" };

            /// <summary>A path to the service account's .p12 file that
            /// you download from Google Developer Console's Auth page.
            /// </summary>
            public string ServiceAccountP12KeyPath = Constants.ServiceAccountP12KeyPath;

            /// <summary>A place to log events.  Can be null.
            /// </summary>
            public LogWriter LogWriter = null;
        }

        public static bool AmIRunningOnGoogleComputeEngine()
        {
            var session = CimSession.Create(null);
            foreach (var instance in session.QueryInstances(@"ROOT\CIMV2", "WQL",
                "SELECT * FROM Win32_ComputerSystem"))
            {
                var manufacturer = instance.CimInstanceProperties["Manufacturer"].Value;
                var model = instance.CimInstanceProperties["Model"].Value;
                return manufacturer.Equals("Google") && model.Equals("Google");
            }
            return false;
        }

        /// <summary>
        /// Gets credentials you can use to authenticate with Google services.
        /// </summary>
        public static IConfigurableHttpClientInitializer GetCredentials(
            Initializer init)
        {
            if (AmIRunningOnGoogleComputeEngine())
            {
                return new ComputeCredential(new ComputeCredential.Initializer());
            }
            if (init.LogWriter != null && init.LogWriter.IsLoggingEnabled())
                init.LogWriter.Write("Using credentials from " + init.ServiceAccountP12KeyPath + ".");
            var certificate = new X509Certificate2(init.ServiceAccountP12KeyPath, "notasecret",
                X509KeyStorageFlags.Exportable);
            var credential = new ServiceAccountCredential(
                new ServiceAccountCredential.Initializer(init.ServiceAccountEmail)
                {
                    Scopes = init.scopes,
                }.FromCertificate(certificate));
            return credential;
        }
    }
}