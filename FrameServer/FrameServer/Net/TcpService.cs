﻿using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;


namespace Network
{
    public class TcpService
    {
        private NetworkService mService;

        private TcpListener mTCPSocket;

        private int mTCPPort;


        Thread mAcceptThread, mReceiveThread, mSendThread;

        Queue<MessageInfo> mSendMessageQueue = new Queue<MessageInfo>();

        private bool mRunning;
        public bool IsRunning { get { return mRunning; } }


        public event OnReceiveHandler onReceive;
        public event OnTcpConnectHandler onConnect;

        public TcpService(NetworkService service, int port)
        {
            mService = service;
            mTCPPort = port;
            mTCPSocket = new TcpListener(IPAddress.Any, mTCPPort);
        }

        public bool Start()
        {
            if (mRunning)
            {
                return true;
            }

            mAcceptThread = new Thread(AcceptThread);
            mReceiveThread = new Thread(ReceiveThread);
            mSendThread = new Thread(SendThread);

            mRunning = true;


            mAcceptThread.Start();
            mReceiveThread.Start();
            mSendThread.Start();


            return true;
        }

        public void Close()
        {
            if (mTCPSocket != null)
            {
                mTCPSocket.Stop();
                mTCPSocket = null;
            }

            mRunning = false;

            if (mAcceptThread != null)
            {
                mAcceptThread.Abort();
                mAcceptThread = null;
            }

            if (mReceiveThread != null)
            {
                mReceiveThread.Abort();
                mReceiveThread = null;
            }
            if (mSendThread != null)
            {
                mSendThread.Abort();
                mSendThread = null;
            }
        }

        public void Send(MessageInfo message)
        {
            if (message == null)
            {
                return;
            }
            lock (mSendMessageQueue)
            {
                mSendMessageQueue.Enqueue(message);
            }
        }

        void AcceptThread()
        {
            mTCPSocket.Start();

            while (mRunning)
            {
                try
                {
                    Socket s = mTCPSocket.AcceptSocket();
                    if (s != null)
                    {
                        if (onConnect != null)
                        {
                            onConnect(s);
                        }
                    }

                    Thread.Sleep(1);
                }
                catch (Exception e)
                {
                    mService.CatchException(e);
                    throw e;
                }

            }
        }

        void ReceiveThread()
        {
            while (mRunning)
            {
                var sessions = mService.sessions;//一个临时的队列
                for (int i = 0; i < sessions.Count; ++i)
                {
                    if (sessions[i] == null)
                    {
                        continue;
                    }

                    var c = sessions[i];
                    try
                    {
                        if (c.IsConnected == false)
                        {
                            continue;
                        }

                        byte[] headbuffer = new byte[MessageBuffer.MESSAGE_HEAD_SIZE];
                        int receiveSize = c.socket.Receive(headbuffer, MessageBuffer.MESSAGE_HEAD_SIZE, SocketFlags.None);
                        if (receiveSize == 0)
                        {
                            continue;
                        }

                        if (receiveSize != MessageBuffer.MESSAGE_HEAD_SIZE)
                        {
                            continue;
                        }
                        int messageId = BitConverter.ToInt32(headbuffer, MessageBuffer.MESSAGE_ID_OFFSET);
                        int bodySize = BitConverter.ToInt32(headbuffer, MessageBuffer.MESSAGE_BODY_SIZE_OFFSET);

                        if (MessageBuffer.IsValid(headbuffer) == false)
                        {
                            continue;
                        }

                        Console.WriteLine("recv from tcp:" + messageId + " body size:" + bodySize);

                        byte[] messageBuffer = new byte[MessageBuffer.MESSAGE_HEAD_SIZE + bodySize];
                        Array.Copy(headbuffer, 0, messageBuffer, 0, headbuffer.Length);

                        if (bodySize > 0)
                        {
                            int receiveBodySize = c.socket.Receive(messageBuffer, MessageBuffer.MESSAGE_BODY_OFFSET, bodySize, SocketFlags.None);

                            if (receiveBodySize != bodySize)
                            {
                                continue;
                            }
                        }

                        if (onReceive != null)
                        {
                            onReceive(new MessageInfo(new MessageBuffer(messageBuffer), c));
                        }

                    }
                    catch (SocketException e)
                    {
                        mService.Debug(e.Message);
                        c.Disconnect();
                    }
                    catch (Exception e)
                    {
                        mService.CatchException(e);
                        throw e;
                    }
                }

                Thread.Sleep(1);
            }
        }

        void SendThread()
        {
            while (mRunning)
            {

                lock (mSendMessageQueue)
                {
                    while (mSendMessageQueue.Count > 0)
                    {
                        MessageInfo message = mSendMessageQueue.Dequeue();

                        if (message == null) continue;
                        try
                        {
                            message.session.socket.Send(message.buffer.buffer);
                        }
                        catch (SocketException e)
                        {
                            mService.Debug(e.Message);
                            message.session.Disconnect();
                        }
                        catch (Exception e)
                        {
                            mService.CatchException(e);
                            throw e;
                        }
                    }
                }
                Thread.Sleep(1);

            }
        }
    }
}