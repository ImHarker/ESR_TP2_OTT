using System.Text;

namespace ESR.Shared;

public class PacketBuilder
{
    private byte m_OpCode;
    private List<string> m_Arguments = [];

    private bool m_IsPacketDirty;
    
    private byte[] m_Packet = [];
    public byte[] Packet
    {
        get
        {
            if (!m_IsPacketDirty) return m_Packet;
            m_IsPacketDirty = false;
            
            using var stream = new MemoryStream();
            stream.WriteByte(m_OpCode);
            stream.WriteByte((byte)m_Arguments.Count);

            for (var i = 0; i < m_Arguments.Count; i++)
            {
                var buffer = Encoding.UTF8.GetBytes(m_Arguments[i]);
                var bufferLength = BitConverter.GetBytes((short)buffer.Length);
                stream.Write(bufferLength);
                stream.Write(buffer, 0, buffer.Length);
            }
            
            m_Packet = stream.ToArray();
            return m_Packet;
        }        
    }

    public PacketBuilder WriteOpCode(OpCodes opCode)
    {
        m_IsPacketDirty = true;
        m_OpCode = (byte)opCode;
        return this;
    }
    
    public PacketBuilder WriteArgument(string argument)
    {
        m_IsPacketDirty = true;
        m_Arguments.Add(argument);
        return this;
    }
    
    public PacketBuilder WriteArguments(params string[] arguments)
    {
        m_IsPacketDirty = true;
        m_Arguments.AddRange(arguments);
        return this;
    }
    
    public PacketBuilder GetPacket(out byte[] packet)
    {
        packet = Packet;
        return this;
    }
}