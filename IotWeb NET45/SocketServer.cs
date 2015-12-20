﻿using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IotWeb.Common;
using Splat;

namespace IotWeb.Server
{
	public class SocketServer : ISocketServer, IEnableLogger
	{
		// Constants
		private const int BackLog = 5; // Maximum pending requests

		// Instance variables
		private bool m_running;
		private ConnectionHandler m_handler;
        private Socket m_listener;

		public int Port { get; private set; }

		public ConnectionHandler ConnectionRequested
		{
			get
			{
				return m_handler;
			}

			set
			{
				lock (this)
				{
					if (m_running)
						throw new InvalidOperationException("Cannot change handler while server is running.");
					m_handler = value;
				}
			}
		}

		public void Start(int port)
		{
			// Make sure we are not already running
			lock (this)
			{
				if (m_running)
					throw new InvalidOperationException("Socket server is already running.");
				m_running = true;
			}
			Port = port;
            // Set up the listener and bind
            ThreadPool.QueueUserWorkItem((arg) =>
            {
                m_listener = new Socket(SocketType.Stream, ProtocolType.IP);
                m_listener.Bind(new IPEndPoint(IPAddress.Any, port));
                m_listener.Blocking = true;
                m_listener.ReceiveTimeout = 100;
                m_listener.Listen(BackLog);
                // Wait for incoming connections
                while (true)
                {
                    lock (this)
                    {
                        if (!m_running)
                            return;
                    }
                    try
                    {
                        Socket client;
                        try
                        {
                            client = m_listener.Accept();
                        }
                        catch (TimeoutException)
                        {
                            // Allow recheck of running status
                            continue;
                        }
                        if (m_handler != null)
                        {
                            string hostname = "0.0.0.0";
                            IPEndPoint endpoint = client.RemoteEndPoint as IPEndPoint;
                            if (endpoint != null)
                                hostname = endpoint.Address.ToString();
                            ThreadPool.QueueUserWorkItem((e) =>
                            {
                                try
                                {
                                    if (m_handler != null)
                                    {
                                        client.ReceiveTimeout = 0;
                                        m_handler(
                                            this,
                                            hostname,
                                            new NetworkStream(client, FileAccess.Read, false),
                                            new NetworkStream(client, FileAccess.Write, false)
                                            );
                                    }
                                    else
                                        this.Log().Debug("No handler provided for connection requests.");
                                }
                                catch (Exception ex)
                                {
                                    this.Log().Debug("Connection handler failed unexpectedly - {0}", ex.Message);
                                }
                            // Finally, we can close the socket
                            client.Shutdown(SocketShutdown.Both);
                                client.Close();
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        this.Log().Debug("Unexpected error while accepting connection request - {0}", ex.Message);
                    }
                }
            });
		}

		public void Stop()
		{
			lock (this)
			{
				m_running = false;
			}
		}

        /// <summary>
        /// Read with timeout
        /// 
        /// This reads a block of data from the input stream with support
        /// for detecting timeout.
        /// </summary>
        /// <param name="input"></param>
        /// <param name="buffer"></param>
        /// <param name="offset"></param>
        /// <param name="count"></param>
        /// <param name="timedOut"></param>
        /// <returns></returns>
        public int ReadWithTimeout(Stream input, byte[] buffer, int offset, int count, out bool timedOut)
        {
            timedOut = false;
            try
            {
                int result = input.Read(buffer, offset, count);
                return result;
            }
            catch (IOException ex)
            {
                SocketException se = ex.InnerException as SocketException;
                if ((se != null)&& (se.SocketErrorCode == SocketError.TimedOut))
                {
                    timedOut = true;
                    return 0;
                }
                throw ex;
            }
        }
    }
}
