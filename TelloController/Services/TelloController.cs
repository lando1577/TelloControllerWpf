﻿using GalaSoft.MvvmLight;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TelloController.Enums;
using TelloController.Models;

namespace TelloController.Services
{
    public class TelloController : ViewModelBase
    {
        #region Private Fields
        private int _portReceive;
        private int _portSend;
        private string _address;
        private UdpClient _connectionClient;
        private CommandResponse _lastResponse;
        private DateTime _startTime;
        private List<TelloState> _recording;
        private bool _isRecording;
        #endregion

        #region Constructors
        /// <summary>
        /// Constructor with options for address and ports.
        /// </summary>
        /// <param name="address">Tello address to connect to.</param>
        /// <param name="portSend">Tello port to send on.</param>
        /// <param name="portReceive">Tello port to receive on.</param>
        public TelloController(string address, int portSend, int portReceive)
        {
            _address = address;
            _portSend = portSend;
            _portReceive = portReceive;
        }

        /// <summary>
        /// Default constructor.
        /// </summary>
        public TelloController() : this("192.168.10.1", 8889, 8890)
        {
        }
        #endregion

        #region Public Properties
        private bool _isConnected;
        public bool IsListening
        {
            get => _isConnected;
            set
            {
                Set(ref _isConnected, value);
                ResetStartTime();
            }
        }
        #endregion

        #region Public Methods
        public void Connect(Action<CommandResponse> onCommandResponseHandler, Action<TelloState> onStateHandler)
        {
            if (IsListening)
            {
                _connectionClient?.Close();
                IsListening = false;
            }
            else
            {
                _connectionClient = new UdpClient(_portReceive);
                _connectionClient.Connect(_address, _portSend);

                IsListening = true;
                Task.Run(() =>
                {
                    Monitor(onCommandResponseHandler, onStateHandler);
                });
            }
        }

        public void StartRecording()
        {
            ResetStartTime();
            _recording = new List<TelloState>();
            _isRecording = true;
        }

        public List<TelloState> EndRecording()
        {
            _isRecording = false;
            return _recording;
        }

        public void SendCommand(string command)
        {
            if (_connectionClient == null) return;

            var commandBytes = Encoding.UTF8.GetBytes(command);
            _connectionClient.Send(commandBytes, commandBytes.Length);
        }

        public CommandResponse SendWithResponse(string command, int timeout)
        {
            if (_connectionClient == null) return new CommandResponse("Not connected", ResponseCode.NotConnected);

            _lastResponse = null;

            SendCommand(command);

            int attemps = 0;
            while (_lastResponse == null && attemps <= timeout)
            {
                attemps++;
                Thread.Sleep(1000);
            }

            if (attemps > timeout)
            {
                throw new TimeoutException("Expected response was not received within the timeout");
            }

            return _lastResponse;
        } 
        #endregion

        #region Private Methods
        private void ResetStartTime()
        {
            _startTime = DateTime.Now;
        }

        private void Monitor(Action<CommandResponse> onCommandResponseHandler, Action<TelloState> onStateHandler)
        {
            var endpoint = new IPEndPoint(IPAddress.Any, 0);
            try
            {
                while (true)
                {
                    byte[] bytesReceived = _connectionClient.Receive(ref endpoint);
                    string responseString = Encoding.UTF8.GetString(bytesReceived);
                    var currentTime = DateTime.Now;
                    
                    if (responseString.StartsWith("mid:"))
                    {
                        var state = ParseState(currentTime - _startTime, responseString);
                        if (_isRecording)
                        {
                            _recording.Add(state);
                        }
                        onStateHandler.Invoke(state);
                    }
                    else
                    {
                        var commandResponse = new CommandResponse(responseString, ParseResponseCode(responseString));
                        _lastResponse = commandResponse;
                        onCommandResponseHandler.Invoke(commandResponse);
                    }
                }
            }
            catch
            {
                _connectionClient.Close();
                IsListening = false;
            }
        }

        private TelloState ParseState(TimeSpan timeSpan, string rawResponse)
        {            
            return TelloState.Parse((int) timeSpan.TotalMilliseconds, rawResponse);
        }

        private ResponseCode ParseResponseCode(string rawResponse)
        {
            var formatted = rawResponse.TrimEnd('\n').TrimEnd('\r');

            if (formatted == "ok")
            {
                return ResponseCode.Ok;
            }
            else if (formatted.StartsWith("error"))
            {
                return ResponseCode.Error;
            }
            else
            {
                return ResponseCode.Value;
            }
        } 
        #endregion
    }
}
