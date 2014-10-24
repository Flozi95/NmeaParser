﻿//
// Copyright (c) 2014 Morten Nielsen
//
// Licensed under the Microsoft Public License (Ms-PL) (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//    http://opensource.org/licenses/Ms-PL.html
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
//

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using NmeaParser.Nmea;

namespace NmeaParser
{
    public abstract class NmeaDevice : IDisposable
    {
        #region Fields

        private readonly object _lock = new object();
        private string _mMessage = "";
        private Stream _stream;
        private CancellationTokenSource _cancelToken;
        private TaskCompletionSource<bool> _closeTask;
        private readonly Dictionary<string, Dictionary<int, NmeaMessage>> _multiPartMessageCache = new Dictionary<string, Dictionary<int, NmeaMessage>>();

        #endregion

        #region Properties

        public bool IsOpen { get; private set; }

        public event EventHandler<NmeaMessageReceivedEventArgs> MessageReceived;

        public event EventHandler<ExceptionEventArgs> ExceptionOccured;

        #endregion

        #region Abstract Methods

        protected abstract Task<Stream> OpenStreamAsync();

        protected abstract Task CloseStreamAsync(Stream stream);

        #endregion

        #region Methods

        public async Task CloseAsync()
        {
            if (_cancelToken != null)
            {
                _closeTask = new TaskCompletionSource<bool>();
                if (_cancelToken != null)
                {
                    _cancelToken.Cancel();
                }
                _cancelToken = null;
            }
            await _closeTask.Task;
            await CloseStreamAsync(_stream);
            _multiPartMessageCache.Clear();
            _stream = null;
            lock (_lock)
                IsOpen = false;
        }

        public async Task OpenAsync()
        {
            lock (_lock)
            {
                if (IsOpen) return;
                IsOpen = true;
            }
            _cancelToken = new CancellationTokenSource();
            _stream = await OpenStreamAsync();
            StartParser();
            _multiPartMessageCache.Clear();
        }

        private void StartParser()
        {
            var token = _cancelToken.Token;
            Debug.WriteLine("Starting parser...");
            var _ = Task.Run(async () =>
            {
                var stream = _stream;
                byte[] buffer = new byte[1024];
                while (!token.IsCancellationRequested)
                {
                    int readCount = 0;
                    try
                    {
                        readCount = await stream.ReadAsync(buffer, 0, 1024, token).ConfigureAwait(false);
                    }
                    catch (Exception exception)
                    {
                        if (ExceptionOccured != null)
                        {
                            ExceptionOccured(this, new ExceptionEventArgs(exception));
                        }
                    }

                    if (token.IsCancellationRequested)
                    {
                        break;
                    }
                    if (readCount > 0)
                    {
                        OnData(buffer.Take(readCount).ToArray());
                    }
                    await Task.Delay(10, token);
                }
                if (_closeTask != null)
                    _closeTask.SetResult(true);
            });
        }

        private void OnData(byte[] data)
        {
            var nmea = Encoding.UTF8.GetString(data, 0, data.Length);
            string line = null;
            lock (_lock)
            {
                _mMessage += nmea;

                var lineEnd = _mMessage.IndexOf("\n");
                if (lineEnd > -1)
                {
                    line = _mMessage.Substring(0, lineEnd).Trim();
                    _mMessage = _mMessage.Substring(lineEnd + 1);
                }
            }
            if (!string.IsNullOrEmpty(line))
                ProcessMessage(line);
        }

        private void ProcessMessage(string p)
        {
            try
            {
                var msg = NmeaMessage.Parse(p);
                if (msg != null)
                {
                    OnMessageReceived(msg);
                }
            }
            catch { }
        }

        private void OnMessageReceived(NmeaMessage msg)
        {
            var args = new NmeaMessageReceivedEventArgs(msg);
            if (msg is IMultiPartMessage)
            {
                args.IsMultiPart = true;
                var multi = (IMultiPartMessage)msg;
                if (_multiPartMessageCache.ContainsKey(msg.MessageType))
                {
                    var dic = _multiPartMessageCache[msg.MessageType];
                    if (dic.ContainsKey(multi.MessageNumber - 1) && !dic.ContainsKey(multi.MessageNumber))
                    {
                        dic[multi.MessageNumber] = msg;
                    }
                    else //Something is out of order. Clear cache
                        _multiPartMessageCache.Remove(msg.MessageType);
                }
                else if (multi.MessageNumber == 1)
                {
                    _multiPartMessageCache[msg.MessageType] = new Dictionary<int, NmeaMessage>(multi.TotalMessages);
                    _multiPartMessageCache[msg.MessageType][1] = msg;
                }
                if (_multiPartMessageCache.ContainsKey(msg.MessageType))
                {
                    var dic = _multiPartMessageCache[msg.MessageType];
                    if (dic.Count == multi.TotalMessages) //We have a full list
                    {
                        _multiPartMessageCache.Remove(msg.MessageType);
                        args.MessageParts = dic.Values.ToArray();
                    }
                }
            }

            if (MessageReceived != null)
            {
                MessageReceived(this, args);
            }
        }

        #endregion

        #region IDisposable - Implementation

        public void Dispose()
        {
            Dispose(true);
        }

        protected virtual void Dispose(bool force)
        {
            if (_stream != null)
            {
                if (_cancelToken != null)
                {
                    _cancelToken.Cancel();
                    _cancelToken = null;
                }
                CloseStreamAsync(_stream);
                _stream = null;
            }
        }

        #endregion
    }

    public class ExceptionEventArgs : EventArgs
    {
        internal ExceptionEventArgs(Exception exception)
        {
            Exception = exception;
        }
        public Exception Exception { get; private set; }

    }

    public sealed class NmeaMessageReceivedEventArgs : EventArgs
    {
        internal NmeaMessageReceivedEventArgs(NmeaMessage message)
        {
            Message = message;
        }
        public NmeaMessage Message { get; private set; }
        public bool IsMultiPart { get; internal set; }
        public NmeaMessage[] MessageParts { get; internal set; }
    }
}
