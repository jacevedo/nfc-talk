﻿/*
 * Copyright © 2012-2013 Nokia Corporation. All rights reserved.
 * Nokia and Nokia Connecting People are registered trademarks of Nokia Corporation. 
 * Other product and company names mentioned herein may be trademarks
 * or trade names of their respective owners. 
 * See LICENSE.TXT for license information.
 */

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using Windows.Networking.Proximity;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;

namespace NFCTalk
{
    /// <summary>
    /// Peer to peer communication abstraction. Handles creating socket connections
    /// from tap events and also further handles sending and receiving chat messages.
    /// </summary>
    class Communication : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        public Action Connecting;
        public Action Connected;
        public Action UnableToConnect;
        public Action ConnectionInterrupted;
        public Action<Message> MessageReceived;
        public Action Searching;
        public Action SearchFinished;

        private enum ConnectionStatusValue
        {
            NotConnected = 0,
            Searching,
            Connecting,
            Listening,
            Connected
        };

        private ConnectionStatusValue _status;
        private StreamSocket _socket;
        private DataWriter _writer;
        private DataReader _reader;
        private string _name;
        private IReadOnlyList<PeerInformation> _peers;

        /// <summary>
        /// Chat name of the other device.
        /// </summary>
        public string PeerName
        {
            get
            {
                return _name;
            }

            private set
            {
                if (_name != value)
                {
                    _name = value;

                    Deployment.Current.Dispatcher.BeginInvoke(() =>
                    {
                        if (PropertyChanged != null)
                        {
                            PropertyChanged(this, new PropertyChangedEventArgs("PeerName"));
                        }
                    });
                }
            }
        }

        public IReadOnlyList<PeerInformation> Peers
        {
            get
            {
                return _peers;
            }

            private set
            {
                if (_peers != value)
                {
                    _peers = value;

                    Deployment.Current.Dispatcher.BeginInvoke(() =>
                    {
                        if (PropertyChanged != null)
                        {
                            PropertyChanged(this, new PropertyChangedEventArgs("Peers"));
                        }
                    });
                }
            }
        }

        /// <summary>
        /// Start listening to tap events and attempt to connect if such occurs.
        /// 
        /// Connecting action will be invoked when a tap event happens.
        /// Connected action will be invoked when a connection with another device has been established.
        /// UnableToConnect action will be invoked if connection to another device cannot be made.
        /// ConnectionInterrupted action will be invoked if connection to another device breaks.
        /// MessageReceived action will be invoked when a message from another device has been received.
        /// </summary>
        public void Start()
        {
            if (_status == ConnectionStatusValue.NotConnected)
            {
                _status = ConnectionStatusValue.Searching;

                PeerName = "";

                PeerFinder.DisplayName = NFCTalk.DataContext.Singleton.Settings.Name;
                PeerFinder.TriggeredConnectionStateChanged += TriggeredConnectionStateChanged;
                PeerFinder.ConnectionRequested += ConnectionRequested;

                PeerFinder.Start();
            }
            else
            {
                throw new Exception("Bad state, please stop first");
            }
        }

        /// <summary>
        /// Disconnect currently active session and/or stop listening to tap events.
        /// </summary>
        public void Stop()
        {
            switch (_status)
            {
                case ConnectionStatusValue.Searching:
                case ConnectionStatusValue.Connecting:
                case ConnectionStatusValue.Listening:
                    {
                        PeerFinder.Stop();
                        PeerFinder.TriggeredConnectionStateChanged -= TriggeredConnectionStateChanged;
                        PeerFinder.ConnectionRequested -= ConnectionRequested;

                        _status = ConnectionStatusValue.NotConnected;
                    }
                    break;

                case ConnectionStatusValue.Connected:
                    {
                        if (_socket != null)
                        {
                            _socket.Dispose();
                            _socket = null;
                        }

                        _status = ConnectionStatusValue.NotConnected;
                    }
                    break;
            }
        }

        public void Disconnect()
        {
            if (_status == ConnectionStatusValue.Connected)
            {
                if (_socket != null)
                {
                    _socket.Dispose();
                    _socket = null;
                }

                PeerFinder.TriggeredConnectionStateChanged += TriggeredConnectionStateChanged;
                PeerFinder.ConnectionRequested += ConnectionRequested;

                _status = ConnectionStatusValue.Searching;
            }
        }

        public async void Search()
        {
            if (_status == ConnectionStatusValue.Searching)
            {
                Peers = null;

                try
                {
                    if (Searching != null)
                    {
                        Searching();
                    }

                    Peers = await PeerFinder.FindAllPeersAsync();

                    if (SearchFinished != null)
                    {
                        SearchFinished();
                    }
                }
                catch (Exception ex)
                {
                    if (UnableToConnect != null)
                    {
                        UnableToConnect();
                    }
                }
            }
            else
            {
                throw new Exception("Bad state, please start first");
            }
        }

        public async void Connect(PeerInformation peer)
        {
            _status = ConnectionStatusValue.Connecting;

            try
            {
                if (Connecting != null)
                {
                    Connecting();
                }

                _socket = await PeerFinder.ConnectAsync(peer);

                if (_socket != null)
                {
                    PeerFinder.TriggeredConnectionStateChanged -= TriggeredConnectionStateChanged;
                    PeerFinder.ConnectionRequested -= ConnectionRequested;

                    _status = ConnectionStatusValue.Connected;

                    _writer = new DataWriter(_socket.OutputStream);
                    _writer.UnicodeEncoding = Windows.Storage.Streams.UnicodeEncoding.Utf8;
                    _writer.ByteOrder = Windows.Storage.Streams.ByteOrder.LittleEndian;

                    _reader = new DataReader(_socket.InputStream);
                    _reader.UnicodeEncoding = Windows.Storage.Streams.UnicodeEncoding.Utf8;
                    _reader.ByteOrder = Windows.Storage.Streams.ByteOrder.LittleEndian;

                    ListenAsync();

                    SendNameAsync(NFCTalk.DataContext.Singleton.Settings.Name);

                    if (Connected != null)
                    {
                        Connected();
                    }
                }
                else if (UnableToConnect != null)
                {
                    UnableToConnect();
                }
            }
            catch (Exception ex)
            {
                if (UnableToConnect != null)
                {
                    UnableToConnect();
                }
            }
        }

        /// <summary>
        /// Event handler to be executed when PeerFinder's TriggeredConnectionStateChanged changes.
        /// </summary>
        private void TriggeredConnectionStateChanged(object sender, TriggeredConnectionStateChangedEventArgs e)
        {
            switch (e.State)
            {
                case TriggeredConnectState.PeerFound: System.Diagnostics.Debug.WriteLine("PeerFound");
                    {
                        if (Connecting != null)
                        {
                            Connecting();
                        }
                    }
                    break;

                case TriggeredConnectState.Connecting: System.Diagnostics.Debug.WriteLine("Connecting");
                    {
                        _status = ConnectionStatusValue.Connecting;
                    }
                    break;

                case TriggeredConnectState.Listening: System.Diagnostics.Debug.WriteLine("Listening");
                    {
                        _status = ConnectionStatusValue.Listening;
                    }
                    break;

                case TriggeredConnectState.Completed: System.Diagnostics.Debug.WriteLine("Completed");
                    {
                        PeerFinder.TriggeredConnectionStateChanged -= TriggeredConnectionStateChanged;
                        PeerFinder.ConnectionRequested -= ConnectionRequested;
                        
                        _socket = e.Socket;

                        _writer = new DataWriter(_socket.OutputStream);
                        _writer.UnicodeEncoding = Windows.Storage.Streams.UnicodeEncoding.Utf8;
                        _writer.ByteOrder = Windows.Storage.Streams.ByteOrder.LittleEndian;

                        _reader = new DataReader(_socket.InputStream);
                        _reader.UnicodeEncoding = Windows.Storage.Streams.UnicodeEncoding.Utf8;
                        _reader.ByteOrder = Windows.Storage.Streams.ByteOrder.LittleEndian;

                        _status = ConnectionStatusValue.Connected;
                        
                        ListenAsync();

                        SendNameAsync(NFCTalk.DataContext.Singleton.Settings.Name);

                        if (Connected != null)
                        {
                            Connected();
                        }
                    }
                    break;

                case TriggeredConnectState.Canceled: System.Diagnostics.Debug.WriteLine("Canceled");
                    {
                        if (UnableToConnect != null)
                        {
                            UnableToConnect();
                        }
                    }
                    break;

                case TriggeredConnectState.Failed: System.Diagnostics.Debug.WriteLine("Failed");
                    {
                        if (UnableToConnect != null)
                        {
                            UnableToConnect();
                        }
                    }
                    break;
            }
        }

        private async void ConnectionRequested(object sender, ConnectionRequestedEventArgs e)
        {
            try
            {
                _socket = await PeerFinder.ConnectAsync(e.PeerInformation);

                PeerFinder.TriggeredConnectionStateChanged -= TriggeredConnectionStateChanged;
                PeerFinder.ConnectionRequested -= ConnectionRequested;

                _writer = new DataWriter(_socket.OutputStream);
                _writer.UnicodeEncoding = Windows.Storage.Streams.UnicodeEncoding.Utf8;
                _writer.ByteOrder = Windows.Storage.Streams.ByteOrder.LittleEndian;

                _reader = new DataReader(_socket.InputStream);
                _reader.UnicodeEncoding = Windows.Storage.Streams.UnicodeEncoding.Utf8;
                _reader.ByteOrder = Windows.Storage.Streams.ByteOrder.LittleEndian;

                _status = ConnectionStatusValue.Connected;

                ListenAsync();

                SendNameAsync(NFCTalk.DataContext.Singleton.Settings.Name);

                if (Connected != null)
                {
                    Connected();
                }
            }
            catch (Exception ex)
            {
                if (UnableToConnect != null)
                {
                    UnableToConnect();
                }
            }
        }

        /// <summary>
        /// Sends chat name to the other device.
        /// </summary>
        /// <param name="name">Chat name to send</param>
        private async Task SendNameAsync(string name)
        {
            try
            {
                _writer.WriteUInt32(0); // protocol version
                _writer.WriteUInt32(0); // operation identifier

                uint length = _writer.MeasureString(name);
                _writer.WriteUInt32(length);
                _writer.WriteString(name);

                await _writer.StoreAsync();
            }
            catch (Exception)
            {
                if (ConnectionInterrupted != null)
                {
                    ConnectionInterrupted();
                }
            }
        }

        /// <summary>
        /// Sends a chat message to the other device.
        /// </summary>
        /// <param name="m">Message to send</param>
        public async Task SendMessageAsync(Message m)
        {
            try
            {
                if (m.Text.Length > 0)
                {
                    _writer.WriteUInt32(0); // protocol version
                    _writer.WriteUInt32(1); // operation identifier

                    uint length = _writer.MeasureString(m.Text);
                    _writer.WriteUInt32(length);
                    _writer.WriteString(m.Text);

                    await _writer.StoreAsync();
                }
            }
            catch (Exception)
            {
                if (ConnectionInterrupted != null)
                {
                    ConnectionInterrupted();
                }
            }
        }

        /// <summary>
        /// Attempts to load the requested amount of bytes. Throws an Exception if the connection
        /// is no longer up.
        /// </summary>
        /// <param name="length">Amount of incoming bytes to load</param>
        private async Task GuaranteedLoadAsync(uint length)
        {
            DataReaderLoadOperation op;

            while (length != _reader.UnconsumedBufferLength)
            {
                op = _reader.LoadAsync(length - _reader.UnconsumedBufferLength);

                if (await op.AsTask<uint>() == 0)
                {
                    throw new Exception();
                }
            }
        }

        /// <summary>
        /// Listens to incoming transmissions.
        /// 
        /// PeerName property is updates if a chat name is received from the other device.
        /// MessageReceived action will be invoked when a message from another device has been received.
        /// ConnectionInterrupted action will be invoked if connection to another device breaks.
        /// </summary>
        private async Task ListenAsync()
        {
            try
            {
                while (true)
                {
                    await GuaranteedLoadAsync(sizeof(UInt32));
                    uint version = _reader.ReadUInt32();

                    if (version == 0)
                    {
                        await GuaranteedLoadAsync(sizeof(UInt32));
                        uint operation = _reader.ReadUInt32();

                        switch (operation)
                        {
                            case 0: // name
                                {
                                    await GuaranteedLoadAsync(sizeof(UInt32));
                                    uint length = _reader.ReadUInt32();
                                    await GuaranteedLoadAsync(length);
                                    string name = _reader.ReadString(length);

                                    PeerName = name;
                                }
                                break;

                            case 1: // message
                                {
                                    await GuaranteedLoadAsync(sizeof(UInt32));
                                    uint length = _reader.ReadUInt32();

                                    await GuaranteedLoadAsync(length);
                                    string text = _reader.ReadString(length);

                                    Message m = new Message()
                                    {
                                        Name = PeerName,
                                        Text = text,
                                        Direction = Message.DirectionValue.In
                                    };

                                    if (MessageReceived != null)
                                    {
                                        MessageReceived(m);
                                    }
                                }
                                break;
                        }
                    }
                    else
                    {
                        throw new Exception("Protocol version mismatch");
                    }
                }
            }
            catch (Exception ex)
            {
                if (ConnectionInterrupted != null)
                {
                    ConnectionInterrupted();
                }
            }
        }
    }
}