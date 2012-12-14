using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Harvester.Core.Messaging.Sources.Network
{
    public class UdpMessageListener : MessageListener
    {
        private readonly UdpServerMessageBuffer messageBuffer;

        public UdpMessageListener(IProcessMessages messageProcessor, IConfigureListeners configuration)
            : this(
                GetSource(configuration),
                messageProcessor,
                new UdpServerMessageBuffer(GetSource(configuration), GetEndPoint(configuration)))
        {
        }

        private UdpMessageListener(
            string source,
            IProcessMessages messageProcessor,
            UdpServerMessageBuffer messageBuffer)
            : base(source, messageProcessor, new UdpMessageReader(messageBuffer))
        {
            this.messageBuffer = messageBuffer;
        }

        private static IPEndPoint GetEndPoint(IConfigureListeners configuration)
        {
            var uri = new Uri(configuration.Binding.Replace("+", "any"));

            if (uri.Scheme != "udp")
                throw new NotSupportedException("Not suppoerted protocol: " + uri.Scheme);

            var ipAddress = uri.Host == "any" ? IPAddress.Any : IPAddress.Parse(uri.Host);

            return new IPEndPoint(ipAddress, uri.Port);
        }

        private static string GetSource(IConfigureListeners configuration)
        {
            Verify.NotNull(configuration, "configuration");

            return configuration.Binding;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                this.messageBuffer.Dispose();

            base.Dispose(disposing);
        }

        private sealed class Log4JMessage : IMessage
        {
            public Log4JMessage(string source, byte[] data)
            {
                this.Timestamp = DateTime.Now;
                this.Message = Encoding.UTF8.GetString(data);
                this.Source = source;
            }

            public DateTime Timestamp { get; private set; }
            public int ProcessId { get { return 0; } }
            public string Message { get; private set; }
            public string Source { get; private set; }
        }

        private class UdpMessageReader : IReadMessages
        {
            private readonly MessageBuffer messageBuffer;

            public UdpMessageReader(MessageBuffer messageBuffer)
            {
                this.messageBuffer = messageBuffer;
            }

            public IMessage Current { get; private set; }

            public bool ReadNext()
            {
                try
                {
                    var data = this.messageBuffer.Read();

                    if (data == null)
                    {
                        this.Current = null;
                        return false;
                    }

                    this.Current = new Log4JMessage(this.messageBuffer.Name, data);

                    return true;
                } catch (Exception exception)
                {
                    if (exception is DecoderFallbackException ||
                        exception is InvalidDataException ||
                        exception is OverflowException)
                        return false;

                    throw;
                }
            }
        }

        private class UdpServerMessageBuffer : MessageBuffer
        {
            private readonly UdpClient udpClient;
            private bool closing;

            public UdpServerMessageBuffer(string name, IPEndPoint endPoint) : base(name)
            {
                this.udpClient = new UdpClient(endPoint);
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    this.closing = true;
                    this.udpClient.Close();
                }
            }

            protected override byte[] ReadMessage()
            {
                var endPoint = new IPEndPoint(IPAddress.Any, 0);

                while (!this.closing)
                {
                    this.udpClient.Client.ReceiveTimeout = 1000;
                    try
                    {
                        return this.udpClient.Receive(ref endPoint);
                    } catch (SocketException exception)
                    {
                        if (exception.SocketErrorCode != SocketError.TimedOut &&
                            exception.SocketErrorCode != SocketError.Interrupted)
                            throw;
                    }
                }

                return null;
            }

            protected override void WriteMessage(byte[] message)
            {
                throw new NotSupportedException();
            }
        }
    }
}