// Copyright(c) 2016 Google Inc.
//
// Licensed under the Apache License, Version 2.0 (the "License"); you may not
// use this file except in compliance with the License. You may obtain a copy of
// the License at
//
// http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
// WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
// License for the specific language governing permissions and limitations under
// the License.

using Google.Apis.Pubsub.v1;
using Google.Apis.Pubsub.v1.Data;
using Microsoft.Practices.EnterpriseLibrary.Logging;
using Microsoft.Practices.EnterpriseLibrary.Logging.Formatters;
using Microsoft.Practices.EnterpriseLibrary.Logging.TraceListeners;
using ShoutLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace ShoutLibTest
{
    /// <summary>
    /// Intercepts HTTP Requests and returns a custom HTTP Response.
    /// </summary>
    public class MockMessageHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage> Handler { get; set; }

        protected override Task<HttpResponseMessage>
            SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Handler == null ? Task.FromResult(new HttpResponseMessage())
                : Task.FromResult(Handler(request));
        }
    }

    public class Test
    {
        private readonly PubsubService _pubsub;
        private readonly string _topicPath;
        private readonly string _subscriptionPath;
        private readonly Shouter _shouter;

        // Log to memory so we can inspect it.
        private readonly MemoryStream _logStream;

        private static bool s_createdTopicAndSubscription = false;

        // Intercepts HTTP Requests and returns a custom HTTP Response.
        private readonly MockMessageHandler _httpMessageHandler;

        public Test()
        {
            _httpMessageHandler = new MockMessageHandler();
            _logStream = new MemoryStream();
            var init = Shouter.Initializer.CreateDefault();
            init.LogWriter = CreateLogWriter(_logStream);
            init.HttpClient = new HttpClient(_httpMessageHandler);
            _pubsub = init.PubsubService;
            init.SubscriptionName += "-test";
            init.ProjectId = Environment.GetEnvironmentVariable("GOOGLE_PROJECT_ID");
            _topicPath = $"projects/{init.ProjectId}/topics/{init.SubscriptionName}-topic";
            _subscriptionPath = $"projects/{init.ProjectId}/subscriptions/{init.SubscriptionName}";
            CreateTopicAndSubscriptionOnce();
            ClearSubscription();
            _shouter = new Shouter(init);
        }

        /// <summary>
        /// Create a LogWrite that writes to the MemoryStream.
        /// </summary>
        /// <param name="memStream">Log will be written here.</param>
        /// <returns>A new LogWriter</returns>
        private static LogWriter CreateLogWriter(MemoryStream memStream)
        {
            var config = new LoggingConfiguration();
            var source = config.AddLogSource(
                "All", System.Diagnostics.SourceLevels.All, true);
            source.AddAsynchronousTraceListener(new FormattedTextWriterTraceListener(
                memStream, new TextFormatter("{message}\n")));
            return new LogWriter(config);
        }

        /// <summary>
        /// Creates the topic and subscription that we need for these tests.
        /// </summary>
        private void CreateTopicAndSubscriptionOnce()
        {
            if (s_createdTopicAndSubscription)
                return;
            s_createdTopicAndSubscription = true;
            try
            {
                _pubsub.Projects.Topics.Create(new Topic() { Name = _topicPath }, _topicPath)
                    .Execute();
            }
            catch (Google.GoogleApiException e)
            {
                // A 409 is ok.  It means the topic already exists.
                if (e.Error.Code != 409)
                    throw;
            }
            try
            {
                _pubsub.Projects.Subscriptions.Create(new Subscription()
                {
                    Name = _subscriptionPath,
                    Topic = _topicPath
                }, _subscriptionPath).Execute();
            }
            catch (Google.GoogleApiException e)
            {
                // A 409 is ok.  It means the subscription already exists.
                if (e.Error.Code != 409)
                    throw;
            }
        }

        /// <summary>
        /// Purge all the messages currently in the subscription.
        /// </summary>
        private void ClearSubscription()
        {
            while (true)
            {
                var pullResponse = _pubsub.Projects.Subscriptions.Pull(
                    new PullRequest()
                    {
                        MaxMessages = 100,
                        ReturnImmediately = true
                    }, _subscriptionPath).Execute();
                if (pullResponse.ReceivedMessages == null
                    || pullResponse.ReceivedMessages.Count == 0)
                    break;
                _pubsub.Projects.Subscriptions.Acknowledge(new AcknowledgeRequest()
                {
                    AckIds = (from msg in pullResponse.ReceivedMessages select msg.AckId).ToList()
                }, _subscriptionPath).Execute();
            }
        }

        /// <summary>
        /// Encode the text that we want to be Shouted.
        /// </summary>
        /// <param name="message"></param>
        /// <returns></returns>
        private string EncodeData(string message)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(message));
        }

        /// <summary>
        /// Get all the text written to the log.
        /// </summary>
        /// <returns>The text written to the log.</returns>
        private string GetLogText()
        {
            _logStream.Seek(0, SeekOrigin.Begin);
            var buffer = new byte[_logStream.Length];
            var bytesRead = _logStream.Read(buffer, 0, buffer.Length);
            return Encoding.UTF8.GetString(buffer);
        }

        /// <summary>
        /// Calculate a unix timestamp for secondsFromNow.
        /// </summary>
        /// <param name="secondsFromNow">How many seconds from now?</param>
        /// <returns>A unix timestamp.</returns>
        static public long FutureUnixTime(long secondsFromNow)
        {
            var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var elapsed = DateTime.UtcNow - epoch;
            return (long)elapsed.TotalSeconds + secondsFromNow;
        }

        /// <summary>
        /// Test a corrupt message arriving in the subscription.
        /// </summary>
        [Fact]
        private void TestMissingAttributes()
        {
            _pubsub.Projects.Topics.Publish(new PublishRequest()
            {
                Messages = new PubsubMessage[] { new PubsubMessage()
                {
                    Data = EncodeData("hello")
                } }
            }, _topicPath).Execute();
            _shouter.ShoutOrThrow(new System.Threading.CancellationTokenSource().Token);
            Assert.True(GetLogText().Contains("Bad shout request message attributes"));
        }

        /// <summary>
        /// Test a request that has already expired.
        /// </summary>
        [Fact]
        private void TestExpired()
        {
            _httpMessageHandler.Handler = request =>
            {
                Assert.Equal("https://localhost/", request.RequestUri.OriginalString);
                string content = request.Content.ReadAsStringAsync().Result;
                var query = System.Web.HttpUtility.ParseQueryString(content);
                Assert.Equal("AladdinsCastle", query["token"]);
                return new HttpResponseMessage();
            };
            _pubsub.Projects.Topics.Publish(new PublishRequest()
            {
                Messages = new PubsubMessage[] { new PubsubMessage()
                {
                    Data = EncodeData("hello"),
                    Attributes = new Dictionary<string, string>
                    {
                        {"postStatusUrl", "https://localhost/" },
                        {"postStatusToken", "AladdinsCastle" },
                        {"deadline", "0" },
                    }
                } }
            }, _topicPath).Execute();
            _shouter.ShoutOrThrow(new System.Threading.CancellationTokenSource().Token);
            string logText = GetLogText();
            Assert.Contains("Request timed out.", logText);
        }

        /// <summary>
        /// Test a simple success.
        /// </summary>
        [Fact]
        private void TestHello()
        {
            bool sawHello = false;
            _httpMessageHandler.Handler = request =>
            {
                Assert.Equal("https://localhost/", request.RequestUri.OriginalString);
                string content = request.Content.ReadAsStringAsync().Result;
                var query = System.Web.HttpUtility.ParseQueryString(content);
                Assert.Equal("AladdinsCastle", query["token"]);
                if ("success" == query["status"])
                    sawHello = "HELLO" == query["result"];
                return new HttpResponseMessage();
            };
            _pubsub.Projects.Topics.Publish(new PublishRequest()
            {
                Messages = new PubsubMessage[] { new PubsubMessage()
                {
                    Data = EncodeData("hello"),
                    Attributes = new Dictionary<string, string>
                    {
                        {"postStatusUrl", "https://localhost/" },
                        {"postStatusToken", "AladdinsCastle" },
                        {"deadline",  FutureUnixTime(30).ToString() },
                    }
                } }
            }, _topicPath).Execute();
            _shouter.ShoutOrThrow(new System.Threading.CancellationTokenSource().Token);
            Assert.True(sawHello);
            string logText = GetLogText();
            Assert.False(logText.Contains("Request timed out."), logText);
            Assert.False(logText.Contains("Fatal."), logText);
        }

        /// <summary>
        /// Test a simple failure.
        /// </summary>
        [Fact]
        private void TestChickenFailure()
        {
            bool succeeded = false;
            _httpMessageHandler.Handler = request =>
            {
                Assert.Equal("https://localhost/", request.RequestUri.OriginalString);
                string content = request.Content.ReadAsStringAsync().Result;
                var query = System.Web.HttpUtility.ParseQueryString(content);
                Assert.Equal("AladdinsCastle", query["token"]);
                if ("success" == query["status"])
                    succeeded = true;
                return new HttpResponseMessage();
            };
            _pubsub.Projects.Topics.Publish(new PublishRequest()
            {
                Messages = new PubsubMessage[] { new PubsubMessage()
                {
                    Data = EncodeData("chickens"),
                    Attributes = new Dictionary<string, string>
                    {
                        {"postStatusUrl", "https://localhost/" },
                        {"postStatusToken", "AladdinsCastle" },
                        {"deadline",  FutureUnixTime(30).ToString() },
                    }
                } }
            }, _topicPath).Execute();
            _shouter.ShoutOrThrow(new System.Threading.CancellationTokenSource().Token);
            string logText = GetLogText();
            Assert.False(succeeded);
            Assert.Contains("Fatal", logText);
            Assert.Contains("Oh no!", logText);
        }
    }
}