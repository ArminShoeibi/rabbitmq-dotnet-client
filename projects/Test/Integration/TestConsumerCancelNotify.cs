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

using System.Linq;
using System.Threading;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Xunit;
using Xunit.Abstractions;

namespace Test.Integration
{
    public class TestConsumerCancelNotify : IntegrationFixture
    {
        private readonly ManualResetEventSlim _latch = new ManualResetEventSlim();
        private bool _notifiedCallback;
        private bool _notifiedEvent;
        private string _consumerTag;

        public TestConsumerCancelNotify(ITestOutputHelper output) : base(output)
        {
        }

        [Fact]
        public void TestConsumerCancelNotification()
        {
            TestConsumerCancel("queue_consumer_cancel_notify", false, ref _notifiedCallback);
        }

        [Fact]
        public void TestConsumerCancelEvent()
        {
            TestConsumerCancel("queue_consumer_cancel_event", true, ref _notifiedEvent);
        }

        [Fact]
        public void TestCorrectConsumerTag()
        {
            string q1 = GenerateQueueName();
            string q2 = GenerateQueueName();

            _channel.QueueDeclare(q1, false, false, false, null);
            _channel.QueueDeclare(q2, false, false, false, null);

            EventingBasicConsumer consumer = new EventingBasicConsumer(_channel);
            string consumerTag1 = _channel.BasicConsume(q1, true, consumer);
            string consumerTag2 = _channel.BasicConsume(q2, true, consumer);

            string notifiedConsumerTag = null;
            consumer.ConsumerCancelled += (sender, args) =>
            {
                notifiedConsumerTag = args.ConsumerTags.First();
                _latch.Set();
            };

            _channel.QueueDelete(q1);
            Wait(_latch, "ConsumerCancelled event");
            Assert.Equal(consumerTag1, notifiedConsumerTag);

            _channel.QueueDelete(q2);
        }

        private void TestConsumerCancel(string queue, bool EventMode, ref bool notified)
        {
            _channel.QueueDeclare(queue, false, true, false, null);
            IBasicConsumer consumer = new CancelNotificationConsumer(_channel, this, EventMode);
            string actualConsumerTag = _channel.BasicConsume(queue, false, consumer);

            _channel.QueueDelete(queue);
            Wait(_latch, "HandleBasicCancel / Cancelled event");
            Assert.True(notified);
            Assert.Equal(actualConsumerTag, _consumerTag);
        }

        private class CancelNotificationConsumer : DefaultBasicConsumer
        {
            private readonly TestConsumerCancelNotify _testClass;
            private readonly bool _eventMode;

            public CancelNotificationConsumer(IChannel channel, TestConsumerCancelNotify tc, bool EventMode)
                : base(channel)
            {
                _testClass = tc;
                _eventMode = EventMode;
                if (EventMode)
                {
                    ConsumerCancelled += Cancelled;
                }
            }

            public override void HandleBasicCancel(string consumerTag)
            {
                if (!_eventMode)
                {
                    _testClass._notifiedCallback = true;
                    _testClass._consumerTag = consumerTag;
                    _testClass._latch.Set();
                }
                base.HandleBasicCancel(consumerTag);
            }

            private void Cancelled(object sender, ConsumerEventArgs arg)
            {
                _testClass._notifiedEvent = true;
                _testClass._consumerTag = arg.ConsumerTags[0];
                _testClass._latch.Set();
            }
        }
    }
}
