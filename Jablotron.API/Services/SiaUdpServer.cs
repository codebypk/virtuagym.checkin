using Jablotron.API.SIA;
using Jablotron.API.SIA.Models;
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Jablotron.API.Services
{
    public sealed class SiaUdpServer : IDisposable
    {
        private readonly SiaUdpServerOptions _options;
        private readonly Dc09Parser _parser;
        private readonly object _sync = new();
        private UdpClient _udpClient;
        private CancellationTokenSource _cts;
        private Task _receiveLoop;
        private bool _disposed;

        public event EventHandler<SiaMessageReceivedEventArgs> MessageReceived;
        public event EventHandler<Exception> ReceiveError;

        public SiaUdpServer(SiaUdpServerOptions options = null, Dc09Parser parser = null)
        {
            _options = options ?? new SiaUdpServerOptions();
            _parser = parser ?? new Dc09Parser();
        }

        public bool IsRunning
        {
            get
            {
                lock (_sync)
                {
                    return _receiveLoop != null && !_receiveLoop.IsCompleted;
                }
            }
        }

        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            lock (_sync)
            {
                if (IsRunning)
                    return Task.CompletedTask;

                _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                _udpClient = new UdpClient(_options.Port);
                _receiveLoop = Task.Run(() => ReceiveLoopAsync(_cts.Token), _cts.Token);
            }

            return Task.CompletedTask;
        }

        public async Task StopAsync()
        {
            Task loop;

            lock (_sync)
            {
                if (!IsRunning)
                    return;

                _cts.Cancel();
                _udpClient.Close();
                loop = _receiveLoop;
            }

            try
            {
                await loop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                Cleanup();
            }
        }

        public async Task SendRspUdpAsync(string host, int port, string seq, string linkNumber, string accountNumber, string vendorData, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            if (string.IsNullOrWhiteSpace(host))
                throw new ArgumentException("host is required.", nameof(host));

            var mid = $"\"RSP\"{seq}L{linkNumber}#{accountNumber}[|{vendorData}]";
            var frame = BuildFrame(mid);

            using var sender = new UdpClient();
            await sender.SendAsync(frame, frame.Length, host, port).WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var received = await _udpClient.ReceiveAsync(cancellationToken).ConfigureAwait(false);
                    var message = _parser.ParseBytes(received.Buffer);

                    MessageReceived?.Invoke(this, new SiaMessageReceivedEventArgs(received.RemoteEndPoint, received.Buffer, message));

                    if (_options.SendAck)
                    {
                        await SendAckAsync(received.RemoteEndPoint, message, cancellationToken).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    ReceiveError?.Invoke(this, ex);
                }
            }
        }

        private async Task SendAckAsync(IPEndPoint remoteEndPoint, Dc09Message message, CancellationToken cancellationToken)
        {
            if (remoteEndPoint == null || message == null)
                return;

            if (string.IsNullOrWhiteSpace(message.AccountNumber) ||
                string.IsNullOrWhiteSpace(message.IdSpravy) ||
                string.IsNullOrWhiteSpace(message.LinkNumber))
            {
                return;
            }

            var ackMid = $"\"ACK\"{message.IdSpravy}L{message.LinkNumber}#{message.AccountNumber}[]";
            var frame = BuildFrame(ackMid);

            await _udpClient.SendAsync(frame, frame.Length, remoteEndPoint).WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        private static byte[] BuildFrame(string mid)
        {
            var midBytes = Encoding.UTF8.GetBytes(mid ?? string.Empty);
            var crc = Crc16Arc(midBytes);
            var lenHex = midBytes.Length.ToString("X4");

            using var output = new MemoryStream(1 + 2 + 4 + midBytes.Length + 1);
            output.WriteByte(0x0A);
            output.WriteByte((byte)((crc >> 8) & 0xFF));
            output.WriteByte((byte)(crc & 0xFF));

            var lenBytes = Encoding.UTF8.GetBytes(lenHex);
            output.Write(lenBytes, 0, lenBytes.Length);
            output.Write(midBytes, 0, midBytes.Length);
            output.WriteByte(0x0D);

            return output.ToArray();
        }

        private static int Crc16Arc(byte[] data)
        {
            var crc = 0x0000;
            foreach (var b in data)
            {
                crc ^= b;
                for (var i = 0; i < 8; i++)
                {
                    crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xA001 : crc >> 1;
                }
            }

            return crc & 0xFFFF;
        }

        private void Cleanup()
        {
            lock (_sync)
            {
                _udpClient?.Dispose();
                _udpClient = null;

                _cts?.Dispose();
                _cts = null;

                _receiveLoop = null;
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(SiaUdpServer));
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            try
            {
                StopAsync().GetAwaiter().GetResult();
            }
            catch
            {
            }

            Cleanup();
        }
    }

    public sealed class SiaMessageReceivedEventArgs : EventArgs
    {
        public SiaMessageReceivedEventArgs(IPEndPoint remoteEndPoint, byte[] rawBytes, Dc09Message message)
        {
            RemoteEndPoint = remoteEndPoint;
            RawBytes = rawBytes ?? Array.Empty<byte>();
            Message = message;
        }

        public IPEndPoint RemoteEndPoint { get; }
        public byte[] RawBytes { get; }
        public Dc09Message Message { get; }
    }
}
