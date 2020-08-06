﻿using Cloudtoid.Interprocess.DomainSocket;
using FluentAssertions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Cloudtoid.Interprocess.Tests
{
    public class DomainSocketTests
    {
        [Fact]
        public void CanCreateUnixDomainSocket()
        {
            using var socket = UnixDomainSocketUtil.CreateUnixDomainSocket();
            socket.AddressFamily.Should().Be(AddressFamily.Unix);
            socket.SocketType.Should().Be(SocketType.Stream);
            socket.ProtocolType.Should().Be(ProtocolType.Unspecified);
        }

        [Fact]
        public void CanSafeDispose()
        {
            var socket = UnixDomainSocketUtil.CreateUnixDomainSocket();
            socket.SafeDispose();
            socket = null;
            socket.SafeDispose();
        }

        [Fact]
        public void CanPerformSocketOperation()
        {
            UnixDomainSocketUtil.SocketOperation(
                callback =>
                {
                    var r = new AsyncResult(true);
                    callback(r);
                    return r;
                },
                _ => true,
                default).Should().BeTrue();
        }

        [Fact]
        public void CanPerformSocketOperationCompletedSynchronously()
        {
            UnixDomainSocketUtil.SocketOperation(
                _ => new AsyncResult(true),
                _ => true,
                default).Should().BeTrue();
        }

        [Fact]
        public void CanSocketOperationTimeout()
        {
            using var source = new CancellationTokenSource();
            source.CancelAfter(200);
            Action action = () => UnixDomainSocketUtil.SocketOperation(
                _ => new AsyncResult(false),
                _ => true,
                source.Token);

            action.Should().ThrowExactly<OperationCanceledException>();
        }

        [Fact]
        public void CanSocketOperationCatchAndRethrowException()
        {
            using var source = new CancellationTokenSource();
            Action action = () => UnixDomainSocketUtil.SocketOperation<bool>(
                callback =>
                {
                    var r = new AsyncResult(true);
                    callback(r);
                    return r;
                },
                _ => throw new NotSupportedException(),
                source.Token);

            action.Should().ThrowExactly<NotSupportedException>();
        }

        [Fact]
        public async Task CanAcceptConnections()
        {
            using var source = new CancellationTokenSource();

            var file = GetRandomNonExistingFilePath();
            var endpoint = new UnixDomainSocketEndPoint(file);

            var connections = new List<Socket>();
            using (var cancelled = new ManualResetEventSlim())
            {
                Task task;
                using (var server = new UnixDomainSocketServer(file))
                using (var client = UnixDomainSocketUtil.CreateUnixDomainSocket())
                {
                    task = StartServerAsync(server, s => connections.Add(s), () => cancelled.Set(), source.Token);
                    Thread.Sleep(100);
                    client.Connect(endpoint);
                    client.Connected.Should().BeTrue();
                }

                cancelled.Wait(500).Should().BeTrue();
                await task;
            }
        }

        private async Task StartServerAsync(
            UnixDomainSocketServer server,
            Action<Socket> onNewConnection,
            Action onCancelled,
            CancellationToken cancellation)
        {
            while (true)
            {
                Socket socket;
                try
                {
                    socket = await server.AcceptAsync(cancellation);
                }
                catch (OperationCanceledException)
                {
                    onCancelled();
                    break;
                }
                onNewConnection(socket);
            }
        }

        private static string GetRandomNonExistingFilePath()
        {
            string result;
            do
            {
                result = Path.GetRandomFileName();
            }
            while (File.Exists(result));

            return result;
        }

        private sealed class AsyncResult : IAsyncResult
        {
            private readonly ManualResetEvent waitHandle;

            public AsyncResult(bool completed)
            {
                IsCompleted = completed;
                waitHandle = new ManualResetEvent(completed);
            }

            public object? AsyncState => null;

            public WaitHandle AsyncWaitHandle => waitHandle;

            public bool CompletedSynchronously => IsCompleted;

            public bool IsCompleted { get; }
        }
    }
}
