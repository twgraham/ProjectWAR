using System.Buffers;

namespace Core.Infrastructure.Network
{
    /// <summary>
    /// Internal struct containing a packet's opcode and payload for queuing.
    /// When backed by pooled memory, call <see cref="Dispose"/> after processing
    /// to return the buffer to the pool.
    /// </summary>
    public readonly struct PacketEnvelope : IDisposable
    {
        /// <summary>
        /// The memory owner backing <see cref="Payload"/>, or null if the payload is not pooled.
        /// </summary>
        private readonly IMemoryOwner<byte>? _owner;
        
        /// <summary>
        /// Gets the packet opcode.
        /// </summary>
        public byte Opcode { get; }

        /// <summary>
        /// Gets the packet payload (opcode already extracted).
        /// </summary>
        public ReadOnlyMemory<byte> Payload { get; }

        /// <summary>
        /// Creates a new packet envelope with an unpooled payload.
        /// </summary>
        /// <param name="opcode">The packet opcode.</param>
        /// <param name="payload">The packet payload.</param>
        public PacketEnvelope(byte opcode, ReadOnlyMemory<byte> payload)
        {
            Opcode = opcode;
            Payload = payload;
            _owner = null;
        }

        /// <summary>
        /// Creates a new packet envelope backed by pooled memory.
        /// The <paramref name="owner"/> is disposed when <see cref="Dispose"/> is called.
        /// </summary>
        /// <param name="opcode">The packet opcode.</param>
        /// <param name="owner">The memory owner from <see cref="MemoryPool{T}.Shared"/>.</param>
        /// <param name="payloadLength">The number of valid bytes in the owner's memory.</param>
        public PacketEnvelope(byte opcode, IMemoryOwner<byte> owner, int payloadLength)
        {
            _owner = owner;
            Opcode = opcode;
            Payload = owner.Memory[..payloadLength];
        }

        /// <summary>
        /// Returns the pooled memory (if any) to the pool.
        /// Safe to call when no pooled memory is held.
        /// </summary>
        public void Dispose()
        {
            _owner?.Dispose();
        }
    }
}
