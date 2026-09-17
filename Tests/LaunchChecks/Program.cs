using System.Buffers.Binary;
using System.IO.Pipes;
using HamiPdf.Services;

using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
string name = "HamiPdf-Test-" + Guid.NewGuid().ToString("N");
string[][] batches = [[], [@"C:\مستنداتي\تقرير عوض.pdf", @"C:\work files\editable.hamipdf"], [@"C:\other.pdf"]];
using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
var receiver = Task.Run(async () =>
{
    foreach (var expected in batches)
    {
        await server.WaitForConnectionAsync(timeout.Token);
        var actual = await LaunchProtocol.ReadAsync(server, timeout.Token);
        if (!actual.SequenceEqual(expected)) throw new Exception("Launch paths changed during IPC.");
        await server.WriteAsync(new byte[]{1}, timeout.Token);
        await server.FlushAsync(timeout.Token);
        server.Disconnect();
    }
});
foreach (var batch in batches)
{
    using var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
    await client.ConnectAsync(timeout.Token);
    await LaunchProtocol.WriteAsync(client, batch, timeout.Token);
    var ack = new byte[1]; await client.ReadExactlyAsync(ack, timeout.Token);
    if (ack[0] != 1) throw new Exception("Request not acknowledged.");
}
await receiver;
foreach (int length in new[]{-1, 0, 1024*1024+1})
{
    byte[] header = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(header,length);
    using var invalid = new MemoryStream(header);
    bool rejected=false;
    try { await LaunchProtocol.ReadAsync(invalid, timeout.Token); } catch(InvalidDataException) { rejected=true; }
    if (!rejected) throw new Exception("Invalid frame accepted.");
}
using(var truncated = new MemoryStream(new byte[]{10,0,0,0,1}))
{
    bool rejected=false;
    try { await LaunchProtocol.ReadAsync(truncated, timeout.Token); } catch(EndOfStreamException) { rejected=true; }
    if (!rejected) throw new Exception("Truncated request accepted.");
}
Console.WriteLine("PASS: named-pipe launch delivery, repeated requests, Unicode/spaced paths, activation, malformed/truncated frame rejection.");
