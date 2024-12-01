using System.Collections.Concurrent;

namespace ESR.Shared {
    public class VideoPacket {
        private const int MaxPacketSize = 1000; 
        private const double Base64ExpansionFactor = 4 / 3; 
        private static readonly TimeSpan FrameExpirationTime = TimeSpan.FromMilliseconds(200); 

        private static readonly ConcurrentDictionary<string, ConcurrentDictionary<int, List<byte[]>>> frameBuffer = new();
        private static readonly ConcurrentDictionary<string, ConcurrentDictionary<int, HashSet<int>>> fragmentTracker = new();
        private static readonly ConcurrentDictionary<string, ConcurrentDictionary<int, DateTime>> frameArrivalTime = new();
        private static readonly ConcurrentDictionary<string, int> expectedFrameNumbers = new();

        public static List<byte[]> BuildVideoPackets(string contentId, int frameNumber, byte[] frame) {
            var packets = new List<byte[]>();
            int adjustedMaxPacketSize = (int)(MaxPacketSize / Base64ExpansionFactor);
            int totalFragments = (int)Math.Ceiling((double)frame.Length / adjustedMaxPacketSize);
            for (int i = 0; i < totalFragments; i++) {
                int chunkSize = Math.Min(adjustedMaxPacketSize, frame.Length - i * adjustedMaxPacketSize);
                byte[] chunk = new byte[chunkSize];
                Array.Copy(frame, i * adjustedMaxPacketSize, chunk, 0, chunkSize);

                var encodedChunk = Convert.ToBase64String(chunk);

                var packet = new PacketBuilder()
                    .WriteOpCode(OpCodes.VideoStream)
                    .WriteArgument(contentId)
                    .WriteArgument(frameNumber.ToString())
                    .WriteArgument(i.ToString())
                    .WriteArgument(totalFragments.ToString())
                    .WriteArgument(encodedChunk)
                    .Packet;

                packets.Add(packet);
            }

            return packets;
        }

        public static byte[] ReadVideoPackets(string contentId, byte[] packet) {
            var frameData = new List<byte>();
            var packetReader = new PacketReader(packet);
            var opCode = packetReader.OpCode;
            var args = packetReader.Arguments;

            if (opCode != OpCodes.VideoStream || args.Length < 5) {
                throw new InvalidOperationException("Invalid packet format.");
            }

            var frameNumber = int.Parse(args[1]);
            var fragmentNumber = int.Parse(args[2]);
            var totalFragments = int.Parse(args[3]);
            var encodedChunk = args[4];

            if (!frameBuffer.ContainsKey(contentId)) {
                frameBuffer[contentId] = new ConcurrentDictionary<int, List<byte[]>>();
                fragmentTracker[contentId] = new ConcurrentDictionary<int, HashSet<int>>();
                frameArrivalTime[contentId] = new ConcurrentDictionary<int, DateTime>();
            }

            if (!frameBuffer[contentId].ContainsKey(frameNumber)) {
                frameBuffer[contentId][frameNumber] = new List<byte[]>();
                fragmentTracker[contentId][frameNumber] = new HashSet<int>();
                frameArrivalTime[contentId][frameNumber] = DateTime.Now;
            }

            int expectedFrameNumber = expectedFrameNumbers.ContainsKey(contentId) ? expectedFrameNumbers[contentId] : 0;

            if (frameNumber > expectedFrameNumber) {
                frameBuffer[contentId].TryRemove(expectedFrameNumber, out _);
                fragmentTracker[contentId].TryRemove(expectedFrameNumber, out _);
                frameArrivalTime[contentId].TryRemove(expectedFrameNumber, out _);
                expectedFrameNumbers[contentId] = frameNumber;
            }

            var chunk = Convert.FromBase64String(encodedChunk);
            frameBuffer[contentId][frameNumber].Add(chunk);
            fragmentTracker[contentId][frameNumber].Add(fragmentNumber);

            if (fragmentTracker[contentId][frameNumber].Count == totalFragments) {
                var allFragments = frameBuffer[contentId][frameNumber]
                    .Select((data, index) => new { FragmentNumber = index, Data = data })
                    .OrderBy(x => x.FragmentNumber)
                    .Select(x => x.Data)
                    .ToArray();

                frameData.AddRange(allFragments.SelectMany(x => x));
                frameBuffer[contentId].TryRemove(frameNumber, out _);
                fragmentTracker[contentId].TryRemove(frameNumber, out _);
                frameArrivalTime[contentId].TryRemove(frameNumber, out _);
            } else if (DateTime.Now - frameArrivalTime[contentId][frameNumber] > FrameExpirationTime) {
                frameBuffer[contentId].TryRemove(frameNumber, out _);
                fragmentTracker[contentId].TryRemove(frameNumber, out _);
                frameArrivalTime[contentId].TryRemove(frameNumber, out _);
            }

            return frameData.ToArray();
        }
    }
}