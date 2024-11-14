using System.Net.Sockets;
using System.Text;

namespace ESR.Shared;

public class PacketReader(NetworkStream stream)
{
    private NetworkStream m_Stream { get; init; } = stream;
    
    private OpCodes? m_OpCode;
    public OpCodes OpCode
    {
        get
        {
            if (m_OpCode != null)
            {
                return m_OpCode.Value;
            }

            ReadOpCode(out var opCode);
            return opCode;
        }
    }
    public PacketReader GetOpCode(out OpCodes opCode)
    {
        opCode = OpCode;
        return this;
    }
    
    private List<string> m_Arguments = [];
    public string[] Arguments
    {
        get
        {
            _ = OpCode;
            
            if (m_ArgumentCount >= 0 && m_Arguments.Count >= m_ArgumentCount)
            {
                return m_Arguments.ToArray();
            }
            
            ReadArguments(out var args);
            m_Arguments = args.ToList();
            
            return args;
        }
    }
    public PacketReader GetArguments(out string[] args)
    {
        args = Arguments;
        return this;
    }
    
    private int m_ArgumentCount { get; set; } = -1;

    private void ReadArguments(out string[] args)
    {
        _ = OpCode;
        if (m_ArgumentCount == -1) m_ArgumentCount = m_Stream.ReadByte();
        if (m_Arguments.Count >= m_ArgumentCount)
        {
            args = m_Arguments.ToArray();
            return;
        }
        
        args = new string[m_ArgumentCount];
        for (var i = 0; i < m_ArgumentCount; i++)
        {
            var lengthBuffer = new byte[2];
            _ = m_Stream.Read(lengthBuffer, 0, 2);
            var length = BitConverter.ToInt16(lengthBuffer);
            var buffer = new byte[length];
            _ = m_Stream.Read(buffer, 0, buffer.Length);
            args[i] = Encoding.UTF8.GetString(buffer);
        }
        
        m_Arguments = args.ToList();
    }
    
    private void ReadOpCode(out OpCodes opCode)
    {
        m_OpCode ??= (OpCodes)m_Stream.ReadByte();
        opCode = m_OpCode ?? throw new InvalidOperationException("[PacketReader] Invalid OpCode");
    }
}