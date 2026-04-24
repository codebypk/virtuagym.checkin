using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Jablotron.API.Services;
using Jablotron.API.SIA;
using Jablotron.API.SIA.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests.Api
{
    [TestClass]
    public class SiaDc09Tests
    {
        [TestMethod]
        public void ParseBytes_ValidDc09Message_ParsesExpectedFields()
        {
            var parser = new Dc09Parser();
            var payload = "SIA-DCS\"1234L1\"[#1111|Nri01^Zone1^/OP001^Servis][NOpened] 12:34:56 01-02-2025\r";
            var bytes = Encoding.UTF8.GetBytes(payload);

            var result = parser.ParseBytes(bytes);

            Assert.AreEqual("1234", result.IdSpravy);
            Assert.AreEqual("1", result.LinkNumber);
            Assert.AreEqual("1111", result.AccountNumber);
            Assert.AreEqual("Nri01", result.NriId);
            Assert.AreEqual("Zone1", result.Zone);
            Assert.AreEqual("Servis", result.User);
            Assert.AreEqual("OP001", result.EventCode);
            Assert.AreEqual("OP", result.EventCodeKey);
            Assert.IsTrue(result.EventDescriptions.Any(d => d.Contains("Opening Report", StringComparison.OrdinalIgnoreCase)));
            Assert.AreEqual("N", result.TypeMessage);
            Assert.AreEqual("Opened", result.ExtraMessage);
            Assert.AreEqual("12:34:56", result.Time);
            Assert.AreEqual("01-02-2025", result.Date);
            Assert.AreEqual("\r", result.Terminator);
            Assert.AreEqual("SIA-DCS", result.ProtocolId);
        }

        [TestMethod]
        public void ParseBytes_Null_ThrowsArgumentNullException()
        {
            var parser = new Dc09Parser();

            try
            {
                parser.ParseBytes(null);
                Assert.Fail("Expected ArgumentNullException was not thrown.");
            }
            catch (ArgumentNullException)
            {
            }
        }

        [TestMethod]
        public void ParseHex_Null_ThrowsArgumentNullException()
        {
            var parser = new Dc09Parser();

            try
            {
                parser.ParseHex(null);
                Assert.Fail("Expected ArgumentNullException was not thrown.");
            }
            catch (ArgumentNullException)
            {
            }
        }

        [TestMethod]
        public async Task Server_ReceivesUdpMessage_RaisesMessageReceived()
        {
            var port = GetFreeUdpPort();
            var parser = new Dc09Parser();
            using var server = new SiaUdpServer(new SiaUdpServerOptions
            {
                Port = port,
                SendAck = false
            }, parser);

            var tcs = new TaskCompletionSource<SiaMessageReceivedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
            server.MessageReceived += (_, args) => { 
                Console.WriteLine($"Message received from {args.RemoteEndPoint}: {args.Message}");
                tcs.TrySetResult(args); 
            };

            await server.StartAsync();
            try
            {
                var payload = "SIA-DCS\"1234L1\"[#1111|Nri01^Zone1^/OP001^Servis][NOpened] 12:34:56 01-02-2025\r";
                var bytes = Encoding.UTF8.GetBytes(payload);

                using (var sender = new UdpClient())
                {
                    await sender.SendAsync(bytes, bytes.Length, "127.0.0.1", port);
                }

                var completed = await Task.WhenAny(tcs.Task, Task.Delay(3000));
                if (completed != tcs.Task)
                    Assert.Fail("MessageReceived was not raised within timeout.");

                var evt = await tcs.Task;
                Assert.IsNotNull(evt);
                Assert.IsNotNull(evt.Message);
                Assert.AreEqual("1234", evt.Message.IdSpravy);
                Assert.AreEqual("Zone1", evt.Message.Zone);
                Assert.IsTrue(evt.RemoteEndPoint.Address.Equals(IPAddress.Loopback) || evt.RemoteEndPoint.Address.Equals(IPAddress.IPv6Loopback));
            }
            finally
            {
                await server.StopAsync();
            }
        }

        private static int GetFreeUdpPort()
        {
            using var udp = new UdpClient(0);
            return ((IPEndPoint)udp.Client.LocalEndPoint).Port;
        }
    }
}
