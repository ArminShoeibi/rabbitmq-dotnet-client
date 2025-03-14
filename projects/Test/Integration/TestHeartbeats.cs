// This source code is dual-licensed under the Apache License, version
// 2.0, and the Mozilla Public License, version 2.0.
//
// The APL v2.0:
//
//---------------------------------------------------------------------------
//   Copyright (c) 2007-2020 VMware, Inc.
//
//   Licensed under the Apache License, Version 2.0 (the "License");
//   you may not use this file except in compliance with the License.
//   You may obtain a copy of the License at
//
//       https://www.apache.org/licenses/LICENSE-2.0
//
//   Unless required by applicable law or agreed to in writing, software
//   distributed under the License is distributed on an "AS IS" BASIS,
//   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//   See the License for the specific language governing permissions and
//   limitations under the License.
//---------------------------------------------------------------------------
//
// The MPL v2.0:
//
//---------------------------------------------------------------------------
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.
//
//  Copyright (c) 2007-2020 VMware, Inc.  All rights reserved.
//---------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using RabbitMQ.Client;
using Xunit;
using Xunit.Abstractions;

namespace Test.Integration
{
    public class TestHeartbeats : IntegrationFixture
    {
        private readonly TimeSpan _heartbeatTimeout = TimeSpan.FromSeconds(2);

        public TestHeartbeats(ITestOutputHelper output) : base(output)
        {
        }

        protected override void SetUp()
        {
            Assert.Null(_connFactory);
            Assert.Null(_conn);
            Assert.Null(_channel);
        }

        [SkippableFact(Timeout = 35000)]
        [Trait("Category", "LongRunning")]
        public void TestThatHeartbeatWriterUsesConfigurableInterval()
        {
            Skip.IfNot(LongRunningTestsEnabled(), "RABBITMQ_LONG_RUNNING_TESTS is not set, skipping test");

            var cf = CreateConnectionFactory();
            cf.RequestedHeartbeat = _heartbeatTimeout;
            cf.AutomaticRecoveryEnabled = false;

            RunSingleConnectionTest(cf);
        }

        [SkippableFact]
        [Trait("Category", "LongRunning")]
        public void TestThatHeartbeatWriterWithTLSEnabled()
        {
            Skip.IfNot(LongRunningTestsEnabled(), "RABBITMQ_LONG_RUNNING_TESTS is not set, skipping test");

            var sslEnv = new SslEnv();
            Skip.IfNot(sslEnv.IsSslConfigured, "SSL_CERTS_DIR and/or PASSWORD are not configured, skipping test");

            var cf = CreateConnectionFactory();
            cf.Port = 5671;
            cf.RequestedHeartbeat = _heartbeatTimeout;
            cf.AutomaticRecoveryEnabled = false;

            cf.Ssl.ServerName = sslEnv.Hostname;
            cf.Ssl.CertPath = sslEnv.CertPath;
            cf.Ssl.CertPassphrase = sslEnv.CertPassphrase;
            cf.Ssl.Enabled = true;

            RunSingleConnectionTest(cf);
        }

        [SkippableFact(Timeout = 90000)]
        [Trait("Category", "LongRunning")]
        public void TestHundredsOfConnectionsWithRandomHeartbeatInterval()
        {
            Skip.IfNot(LongRunningTestsEnabled(), "RABBITMQ_LONG_RUNNING_TESTS is not set, skipping test");

            const ushort connectionCount = 200;

            ThreadPool.GetMinThreads(out int origWorkerThreads, out int origCompletionPortThreads);
            try
            {
                var rnd = new Random();
                var conns = new List<IConnection>();

                // Since we are using the ThreadPool, let's set MinThreads to a high-enough value.
                ThreadPool.SetMinThreads(connectionCount, connectionCount);

                try
                {
                    for (int i = 0; i < connectionCount; i++)
                    {
                        ushort n = Convert.ToUInt16(rnd.Next(2, 6));
                        var cf = CreateConnectionFactory();
                        cf.RequestedHeartbeat = TimeSpan.FromSeconds(n);
                        cf.AutomaticRecoveryEnabled = false;

                        IConnection conn = cf.CreateConnection($"_testDisplayName:{i}");
                        conns.Add(conn);
                        IChannel ch = conn.CreateChannel();
                        conn.ConnectionShutdown += (sender, evt) =>
                            {
                                CheckInitiator(evt);
                            };
                    }
                    SleepFor(60);
                }
                finally
                {
                    foreach (IConnection conn in conns)
                    {
                        conn.Close();
                    }
                }
            }
            finally
            {
                Assert.True(ThreadPool.SetMinThreads(origWorkerThreads, origCompletionPortThreads));
            }
        }

        private void RunSingleConnectionTest(ConnectionFactory cf)
        {
            using (IConnection conn = cf.CreateConnection(_testDisplayName))
            {
                using (IChannel ch = conn.CreateChannel())
                {
                    bool wasShutdown = false;

                    conn.ConnectionShutdown += (sender, evt) =>
                    {
                        lock (conn)
                        {
                            if (InitiatedByPeerOrLibrary(evt))
                            {
                                CheckInitiator(evt);
                                wasShutdown = true;
                            }
                        }
                    };
                    SleepFor(30);

                    Assert.False(wasShutdown, "shutdown event should not have been fired");
                    Assert.True(conn.IsOpen, "connection should be open");
                }
            }
        }

        private bool LongRunningTestsEnabled()
        {
            string s = Environment.GetEnvironmentVariable("RABBITMQ_LONG_RUNNING_TESTS");

            if (String.IsNullOrEmpty(s))
            {
                return false;
            }

            if (Boolean.TryParse(s, out bool enabled))
            {
                return enabled;
            }

            return false;
        }

        private void SleepFor(int t)
        {
            _output.WriteLine("Testing heartbeats, sleeping for {0} seconds", t);
            Thread.Sleep(t * 1000);
        }

        private bool InitiatedByPeerOrLibrary(ShutdownEventArgs evt)
        {
            return !(evt.Initiator == ShutdownInitiator.Application);
        }

        private void CheckInitiator(ShutdownEventArgs evt)
        {
            if (InitiatedByPeerOrLibrary(evt))
            {
                _output.WriteLine(((Exception)evt.Cause).StackTrace);
                string s = string.Format("Shutdown: {0}, initiated by: {1}", evt, evt.Initiator);
                _output.WriteLine(s);
                Assert.Fail(s);
            }
        }
    }
}
