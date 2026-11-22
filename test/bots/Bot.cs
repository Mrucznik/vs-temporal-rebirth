using System.Net.Sockets;
using Vintagestory.API.Common;
using Vintagestory.Common;

namespace TemporalRebirth.Bots;

/// <summary>
/// A player without graphics: speaks just enough of the game's protocol to join a local test server
/// (VerifyPlayerAuth off), chat, respawn and leave. Prints what the server tells it.
/// </summary>
internal sealed class Bot(string name)
{
    // Packet ids, see ClientPackets / ClientSystemStartup / GeneralPacketHandler in VintagestoryLib 1.22.7.
    private const int Identify = 1, PingReply = 2, Chat = 4, RequestJoin = 11, SpecialKey = 12, Leave = 14, ClientLoaded = 26, PlayerReady = 29;

    private TcpClient? tcp;
    private bool joinSent, readySent;

    public string Name => name;
    private string Uid => "bot-" + name;

    public void Connect(string host, int port)
    {
        joinSent = readySent = false;
        tcp = new TcpClient(host, port) { NoDelay = true };
        new Thread(ReadLoop) { IsBackground = true, Name = name }.Start(tcp);
        Send(new Packet_Client
        {
            Id = Identify,
            Identification = new Packet_ClientIdentification
            {
                Playername = name,
                PlayerUID = Uid,
                MdProtocolVersion = "1.22.7",
                ViewDistance = 32,
                NetworkVersion = "1.22.6",
                ShortGameVersion = "1.22.7",
            },
        });
    }

    public void Say(string text) => Send(new Packet_Client { Id = Chat, Chatline = new Packet_ChatLine { Message = text, Groupid = 0 } });

    public void Respawn() => Send(new Packet_Client { Id = SpecialKey, SpecialKey_ = new Packet_ClientSpecialKey { Key_ = 0 } });

    public void Quit()
    {
        Send(new Packet_Client { Id = Leave, Leave = new Packet_ClientLeave { Reason = 0 } });
        Thread.Sleep(200);
        tcp?.Close();
    }

    private void Send(Packet_Client packet)
    {
        byte[] body = Packet_ClientSerializer.SerializeToBytes(packet);
        byte[] frame = new byte[body.Length + 4];
        NetIncomingMessage.WriteInt(frame, body.Length);
        body.CopyTo(frame, 4);
        try
        {
            lock (this)
            {
                tcp!.GetStream().Write(frame);
            }
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or InvalidOperationException)
        {
            Log("send-failed", e.Message);
        }
    }

    private void ReadLoop(object? state)
    {
        var own = (TcpClient)state!;
        try
        {
            NetworkStream stream = own.GetStream();
            byte[] head = new byte[4];
            while (true)
            {
                stream.ReadExactly(head);
                int raw = NetIncomingMessage.ReadInt(head);
                byte[] body = new byte[raw & 0x7FFFFFFF];
                stream.ReadExactly(body);
                if (raw < 0)
                {
                    body = Compression.Decompress(body);
                }

                var packet = new Packet_Server();
                Packet_ServerSerializer.DeserializeBuffer(body, body.Length, packet);
                Handle(packet);
            }
        }
        catch (Exception e) when (e is EndOfStreamException or IOException or ObjectDisposedException)
        {
            Log("closed", "");
        }
        catch (Exception e)
        {
            Log("error", e.ToString());
        }
    }

    private void Handle(Packet_Server p)
    {
        if (p.Ping != null)
        {
            Send(new Packet_Client { Id = PingReply, PingReply = new Packet_ClientPingReply() });
        }

        if (p.ServerReady != null && !joinSent)
        {
            joinSent = true;
            Send(new Packet_Client { Id = RequestJoin, RequestJoin = new Packet_ClientRequestJoin { Language = "en" } });
        }

        if (p.PlayerData?.PlayerUID == Uid && !readySent)
        {
            readySent = true;
            Send(new Packet_Client { Id = ClientLoaded });
            Send(new Packet_Client { Id = PlayerReady });
            Log("joined", "");
        }

        if (p.Chatline != null)
        {
            Log("chat", p.Chatline.Message);
        }

        if (p.ModeChange?.PlayerUID == Uid)
        {
            Log("mode", ((EnumGameMode)p.ModeChange.GameMode).ToString());
        }

        if (p.PlayerDeath != null)
        {
            Log("death", "client " + p.PlayerDeath.ClientId + ", vanilla lives left " + p.PlayerDeath.LivesLeft);
        }

        if (p.DisconnectPlayer != null)
        {
            Log("kicked", p.DisconnectPlayer.DisconnectReason ?? "");
        }
    }

    private void Log(string kind, string text) => Console.WriteLine($"[{name}] {kind}: {text.ReplaceLineEndings(" / ")}");
}
