using System;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace system_net_sockets_deadlock_bug_42718 {
	public abstract class SendReceive<T> : SocketTestHelperBase<T> where T : SocketHelperBase, new() {
		[Fact(Timeout=100000)]
        public async Task ReceiveAsync_ConcurrentShutdownClose_SucceedsOrThrowsAppropriateException()
        {
            if (UsesSync) return;

            for (int i = 0; i < 10000; i++) // run multiple times to attempt to force various interleavings
            {
                (Socket client, Socket server) = CreateConnectedSocketPair();
                /*using (client)*/
                /*using (server)*/
                using (var b = new Barrier(2))
                {
                    Task dispose = Task.Factory.StartNew(() =>
                    {
                        b.SignalAndWait();
                        client.Shutdown(SocketShutdown.Both);
                        client.Close(15);
                    }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

                    Task send = Task.Factory.StartNew(() =>
                    {
                        SendAsync(server, new ArraySegment<byte>(new byte[1])).GetAwaiter().GetResult();
                        b.SignalAndWait();
                        ReceiveAsync(client, new ArraySegment<byte>(new byte[1])).GetAwaiter().GetResult();
                    }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

                    await dispose;
                    Exception error = await Record.ExceptionAsync(() => send);
                    if (error != null)
                    {
                        Assert.True(
                            error is ObjectDisposedException ||
                            error is SocketException ||
                            (error is SEHException /*&& PlatformDetection.IsInAppContainer*/),
                            error.ToString());
                    }
                }
            }
        }

        protected static (Socket, Socket) CreateConnectedSocketPair()
        {
            using (Socket listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
            {
                listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
                listener.Listen(1);

                Socket client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                client.Connect(listener.LocalEndPoint);
                Socket server = listener.Accept();

                return (client, server);
            }
        }

        protected SendReceive(ITestOutputHelper output) : base(output)
        {
        }
	}

	public sealed class SendReceiveTask : SendReceive<SocketHelperTask>
	{
		public SendReceiveTask(ITestOutputHelper output) : base(output) {}
	}
}
