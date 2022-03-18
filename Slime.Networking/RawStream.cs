using Slime.Networking.Utils;

using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Slime.Networking
{
    public class RawStream : Stream
    {
        protected Stream BaseStream { get; set; }
        protected CancellationToken CancellationToken { get; }

        #region Constructors

        public RawStream(Stream stream) : this(stream, CancellationToken.None)
        {
            
        }

        public RawStream(Stream stream, CancellationToken token)
        {
            BaseStream = stream;
            CancellationToken = token;
        }

        public RawStream(byte[] buffer) : this(new MemoryStream(buffer), CancellationToken.None)
        {

        }

        public RawStream(byte[] buffer, CancellationToken token) : this(new MemoryStream(buffer), token)
        {

        }

        #endregion

        private bool IsDataAvailable(Stream stream)
        {
            if(stream is NetworkStream networkStream)
            {
                return networkStream.DataAvailable;
            }
            else if(stream is CipherStream cipherStream)
            {
                return IsDataAvailable(cipherStream.Stream);
            }

            return stream.Position < stream.Length;
        }

        public bool DataAvailable
        {
            get
            {
                return IsDataAvailable(BaseStream);
            }
        }

		public override bool CanRead => BaseStream.CanRead;

        public override bool CanSeek => BaseStream.CanSeek;

        public override bool CanWrite => BaseStream.CanWrite;

        public override long Length => BaseStream.Length;

        public override long Position
        {
            get
            {
                return BaseStream.Position;
            }
            set
            {
                BaseStream.Position = value;
            }
        }

		public override int Read(byte[] buffer, int offset, int count)
        {
            return BaseStream.Read(buffer, offset, count);
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            return BaseStream.Seek(offset, origin);
        }

        public override void SetLength(long value)
        {
            BaseStream.SetLength(value);
        }

        public override void Flush()
        {
            BaseStream.Flush();
        }

		#region Readers

		public override async Task<int> ReadAsync(byte[] buffer,
			int offset,
			int count,
			CancellationToken cancellationToken)
		{
			try
			{
				var read = await BaseStream.ReadAsync(buffer.AsMemory(offset, count), cancellationToken);

				return read;
			}
			catch (Exception)
			{
				return 0;
			}
		}

		public virtual async Task<int> ReadAsync(byte[] buffer, CancellationToken cancellationToken = default)
		{
			try
			{
				var read = await this.BaseStream.ReadAsync(buffer, cancellationToken);

				return read;
			}
			catch (Exception)
			{
				return 0;
			} //TODO better handling of this
		}

		public async Task<byte[]> ReadAsync(int length)
		{
			Memory<byte> buffer = new Memory<byte>(new byte[length]);
			int read = 0;

			do
			{
				int received = await ReadAsync(buffer.Slice(read));

				if (received > 0)
				{
					read += received;
				}
			} while (read < length && !CancellationToken.IsCancellationRequested);

			return buffer.ToArray();
		}

		private int ToInt(byte[] data)
		{
			return IPAddress.NetworkToHostOrder(BitConverter.ToInt32(data, 0));
		}

		public sbyte ReadSignedByte() => (sbyte)this.ReadUnsignedByte();

		public async Task<sbyte> ReadByteAsync() => (sbyte)await this.ReadUnsignedByteAsync();

		public byte ReadUnsignedByte()
		{
			Span<byte> buffer = stackalloc byte[1];
			BaseStream.Read(buffer);

			return buffer[0];
		}

		public async Task<byte> ReadUnsignedByteAsync()
		{
			var buffer = new byte[1];
			await this.ReadAsync(buffer);

			return buffer[0];
		}

		public async Task<int> ReadIntAsync()
		{
			return ToInt(await ReadAsync(4));
		}

		public async Task<float> ReadFloatAsync()
		{
			return EndianUtils.NetworkToHostOrder(BitConverter.ToSingle(await ReadAsync(4), 0));
		}

		public async Task<bool> ReadBoolAsync()
		{
			return (await ReadUnsignedByteAsync()) == 1;
		}

		public async Task<double> ReadDoubleAsync()
		{
			return EndianUtils.NetworkToHostOrder(await ReadAsync(8));
		}

		public async Task<int> ReadVarIntAsync()
		{
			int numRead = 0;
			int result = 0;
			byte read;

			do
			{
				read = await ReadUnsignedByteAsync();

				int value = (read & 0x7f);
				result |= (value << (7 * numRead));

				numRead++;

				if (numRead > 5)
				{
					throw new Exception("VarInt is too big");
				}
			} while ((read & 0x80) != 0);

			return result;
		}

		public async Task<long> ReadVarLongAsync()
		{
			int numRead = 0;
			long result = 0;
			byte read;

			do
			{
				read = await ReadUnsignedByteAsync();
				int value = (read & 0x7f);
				result |= (value << (7 * numRead));

				numRead++;

				if (numRead > 10)
				{
					throw new Exception("VarLong is too big");
				}
			} while ((read & 0x80) != 0);

			return result;
		}

		public async Task<short> ReadShortAsync()
		{
			return IPAddress.NetworkToHostOrder(BitConverter.ToInt16(await ReadAsync(2), 0));
		}

		public async Task<ushort> ReadUShortAsync()
		{
			return EndianUtils.NetworkToHostOrder(BitConverter.ToUInt16(await ReadAsync(2), 0));
		}

		public async Task<string> ReadStringAsync()
		{
			return Encoding.UTF8.GetString(await ReadAsync(await ReadVarIntAsync()));
		}

		public async Task<long> ReadLongAsync()
		{
			return IPAddress.NetworkToHostOrder(BitConverter.ToInt64(await ReadAsync(8), 0));
		}

		public async Task<ulong> ReadULongAsync()
		{
			return EndianUtils.NetworkToHostOrder(BitConverter.ToUInt64(await ReadAsync(8), 0));
		}

		#endregion

		#region Writers

		public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
		{
			await BaseStream.WriteAsync(buffer.AsMemory(offset, count), cancellationToken);
		}

		public override void Write(byte[] buffer, int offset, int count)
		{
			this.BaseStream.Write(buffer, offset, count);
		}

		public async Task WriteAsync(byte[] data)
		{
			await this.BaseStream.WriteAsync(data);
		}

		public async Task WriteByteAsync(byte value)
		{
			await WriteAsync(new byte[] { value });
		}

		public async Task<int> WriteRawVarInt32Async(uint value)
		{
			int written = 0;

			while ((value & -128) != 0)
			{
				await WriteByteAsync((byte)((value & 0x7F) | 0x80));
				value >>= 7;
			}

			await WriteByteAsync((byte)value);
			written++;

			return written;
		}

		public async Task<int> WriteVarIntAsync(int value)
		{
			return await WriteRawVarInt32Async((uint)value);
		}

		public async Task<int> WriteVarLongAsync(long value)
		{
			int write = 0;

			do
			{
				byte temp = (byte)(value & 127);
				value >>= 7;

				if (value != 0)
				{
					temp |= 128;
				}

				await WriteByteAsync(temp);
				write++;
			} while (value != 0);

			return write;
		}

		public async Task WriteIntAsync(int data)
		{
			await WriteAsync(BitConverter.GetBytes(IPAddress.HostToNetworkOrder(data)));
		}

		public async Task WriteStringAsync(string data)
		{
			var stringData = Encoding.UTF8.GetBytes(data);
			await WriteVarIntAsync(stringData.Length);
			await WriteAsync(stringData);
		}

		public async Task WriteShortAsync(short data)
		{
			await WriteAsync(BitConverter.GetBytes(IPAddress.HostToNetworkOrder(data)));
		}

		public async Task WriteUShortAsync(ushort data)
		{
			await WriteAsync(BitConverter.GetBytes(data));
		}

		public async Task WriteBoolAsync(bool data)
		{
			await WriteByteAsync((byte)(data ? 1 : 0));
		}

		public async Task WriteDoubleAsync(double data)
		{
			await WriteAsync(EndianUtils.HostToNetworkOrder(data));
		}

		public async Task WriteFloatAsync(float data)
		{
			await WriteAsync(EndianUtils.HostToNetworkOrder(data));
		}

		public async Task WriteLongAsync(long data)
		{
			await WriteAsync(BitConverter.GetBytes(IPAddress.HostToNetworkOrder(data)));
		}

		public async Task WriteULongAsync(ulong data)
		{
			await WriteAsync(EndianUtils.HostToNetworkOrderLong(data));
		}

		#endregion
	}
}
