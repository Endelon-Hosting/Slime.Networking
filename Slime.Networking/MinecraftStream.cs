using Slime.Networking.Utils;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Slime.Networking
{
    public class MinecraftStream : RawStream
    {
		public static Dictionary<string, string> TypeMap = new()
		{
            {
				"varint",
				"int"
            },
            {
				"buffer",
				"byte[]"
            },
            {
				"pstring",
				"string"
            },
            {
				"u8",
				"byte"
            },
            {
				"u16",
				"ushort"
            },
            {
				"u32",
				"int"
            },
            {
				"u64",
				"ulong"
            },
            {
				"i8",
				"byte"
            }
		};

		public static Dictionary<string, string> MethodMap = new()
		{
            {
				"varint",
				"int"
            },
            {
				"buffer",
				"Buffer"
            },
            {
				"pstring",
				"String"
            },
            {
				"u8",
				"UnsignedByte"
			},
            {
				"u16",
				"ReadUShort"
			},
            {
				"u32",
				"VarInt"
            },
            {
				"u64",
				"ULong"
			},
            {
				"i8",
				"SignedByte"
			}
		};

		public MinecraftStream(Stream baseStream, CancellationToken cancellationToken = default) : base(
			baseStream, cancellationToken)
		{ }

		public MinecraftStream(CancellationToken cancellationToken = default) : this(
			new MemoryStream(), cancellationToken)
		{ }

		public void Read(Span<byte> memory, int count)
		{
			var data = new byte[count];
			BaseStream.Read(data, 0, count);
			data.CopyTo(memory);
		}

		public void Write(in Memory<byte> buffer, int offset, in int bufferLength)
		{
			var bytes = buffer.Slice(offset, bufferLength).ToArray();

			BaseStream.Write(bytes, offset, bytes.Length);
		}

		public byte[] Read(int length)
		{
			if (BaseStream is MemoryStream)
			{
				var dat = new byte[length];
				Read(dat, 0, length);

				return dat;
			}

			//SpinWait s = new SpinWait();
			int read = 0;

			var buffer = new byte[length];

			while (read < buffer.Length && !CancellationToken.IsCancellationRequested)
			{
				int oldRead = read;

				int r = this.Read(buffer, read, length - read);

				if (r == 0) //No data read?
				{
					break;
				}

				read += r;

				if (CancellationToken.IsCancellationRequested)
					throw new ObjectDisposedException("");
			}

			if (read < length)
				throw new EndOfStreamException();

			return buffer;
		}


		public int ReadInt()
		{
			return IPAddress.NetworkToHostOrder(BitConverter.ToInt32(Read(4), 0));
		}

		public float ReadFloat()
		{
			return EndianUtils.NetworkToHostOrder(BitConverter.ToSingle(Read(4), 0));
		}

		public bool ReadBool()
		{
			return ReadUnsignedByte() == 1;
		}

		public double ReadDouble()
		{
			return EndianUtils.NetworkToHostOrder(Read(8));
		}

		public int ReadVarInt()
		{
			return ReadVarInt(out _);
		}

		public int ReadVarInt(out int bytesRead)
		{
			int numRead = 0;
			int result = 0;
			byte read;

			do
			{
				read = (byte)ReadUnsignedByte();
				;

				int value = (read & 0x7f);
				result |= (value << (7 * numRead));

				numRead++;

				if (numRead > 5)
				{
					throw new Exception("VarInt is too big");
				}
			} while ((read & 0x80) != 0);

			bytesRead = numRead;

			return result;
		}

		public long ReadVarLong()
		{
			int numRead = 0;
			long result = 0;
			byte read;

			do
			{
				read = (byte)ReadUnsignedByte();
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

		public short ReadShort()
		{
			return IPAddress.NetworkToHostOrder(BitConverter.ToInt16(Read(2), 0));
		}

		public ushort ReadUShort()
		{
			return EndianUtils.NetworkToHostOrder(BitConverter.ToUInt16(Read(2), 0));
		}

		public ushort[] ReadUShort(int count)
		{
			var us = new ushort[count];

			for (var i = 0; i < us.Length; i++)
			{
				var da = Read(2);
				var d = BitConverter.ToUInt16(da, 0);
				us[i] = d;
			}

			return EndianUtils.NetworkToHostOrder(us);
		}

		public ushort[] ReadUShortLocal(int count)
		{
			var us = new ushort[count];

			for (var i = 0; i < us.Length; i++)
			{
				var da = Read(2);
				var d = BitConverter.ToUInt16(da, 0);
				us[i] = d;
			}

			return us;
		}

		public short[] ReadShortLocal(int count)
		{
			var us = new short[count];

			for (var i = 0; i < us.Length; i++)
			{
				var da = Read(2);
				var d = BitConverter.ToInt16(da, 0);
				us[i] = d;
			}

			return us;
		}

		public string ReadString()
		{
			return Encoding.UTF8.GetString(Read(ReadVarInt()));
		}

		public long ReadLong()
		{
			return IPAddress.NetworkToHostOrder(BitConverter.ToInt64(Read(8), 0));
		}

		public ulong ReadULong()
		{
			return EndianUtils.NetworkToHostOrder(BitConverter.ToUInt64(Read(8), 0));
		}
	}
}