using System;
using System.Runtime.InteropServices;
using System.IO;
using System.Text;
using MiscUtil.Conversion;

namespace MiscUtil.IO
{
	/// <summary>
	/// Equivalent of System.IO.BinaryReader, but with either endianness, depending on
	/// the EndianBitConverter it is constructed with. No data is buffered in the
	/// reader; the client may seek within the stream at will.
	/// </summary>
	public class EndianBinaryReader : IDisposable
	{
		#region Fields not directly related to properties
		/// <summary>
		/// Whether or not this reader has been disposed yet.
		/// </summary>
		bool disposed=false;
		/// <summary>
		/// Decoder to use for string conversions.
		/// </summary>
		Decoder decoder;
		/// <summary>
		/// Buffer used for temporary storage before conversion into primitives
		/// </summary>
		byte[] buffer = new byte[16];
		/// <summary>
		/// Buffer used for temporary storage when reading a single character
		/// </summary>
		char[] charBuffer = new char[1];
		/// <summary>
		/// Minimum number of bytes used to encode a character
		/// </summary>
		int minBytesPerChar;
		#endregion

		#region Constructors
		/// <summary>
		/// Equivalent of System.IO.BinaryWriter, but with either endianness, depending on
		/// the EndianBitConverter it is constructed with.
		/// </summary>
		/// <param name="bitConverter">Converter to use when reading data</param>
		/// <param name="stream">Stream to read data from</param>
		public EndianBinaryReader (EndianBitConverter bitConverter,
								   Stream stream) : this (bitConverter, stream, Encoding.UTF8)
		{
		}

		/// <summary>
		/// Constructs a new binary reader with the given bit converter, reading
		/// to the given stream, using the given encoding.
		/// </summary>
		/// <param name="bitConverter">Converter to use when reading data</param>
		/// <param name="stream">Stream to read data from</param>
		/// <param name="encoding">Encoding to use when reading character data</param>
		public EndianBinaryReader (EndianBitConverter bitConverter,	Stream stream, Encoding encoding)
		{
			if (bitConverter==null)
			{
				throw new ArgumentNullException("bitConverter");
			}
			if (stream==null)
			{
				throw new ArgumentNullException("stream");
			}
			if (encoding==null)
			{
				throw new ArgumentNullException("encoding");
			}
			if (!stream.CanRead)
			{
				throw new ArgumentException("Stream isn't writable", "stream");
			}
			this.stream = stream;
			this.bitConverter = bitConverter;
			this.encoding = encoding;
			this.decoder = encoding.GetDecoder();
			this.minBytesPerChar = 1;

			if (encoding is UnicodeEncoding)
			{
				minBytesPerChar = 2;
			}
		}
		#endregion

		#region Properties
		EndianBitConverter bitConverter;
		/// <summary>
		/// The bit converter used to read values from the stream
		/// </summary>
		public EndianBitConverter BitConverter
		{
			get { return bitConverter; }
		}

		Encoding encoding;
		/// <summary>
		/// The encoding used to read strings
		/// </summary>
		public Encoding Encoding
		{
			get { return encoding; }
		}

		Stream stream;
		/// <summary>
		/// Gets the underlying stream of the EndianBinaryReader.
		/// </summary>
		public Stream BaseStream
		{
			get { return stream; }
		}

		public long Position => stream.Position;
		#endregion
	
		#region Public methods
		/// <summary>
		/// Closes the reader, including the underlying stream..
		/// </summary>
		public void Close()
		{
			Dispose();
		}

		/// <summary>
		/// Seeks within the stream.
		/// </summary>
		/// <param name="offset">Offset to seek to.</param>
		/// <param name="origin">Origin of seek operation.</param>
		public void Seek (int offset, SeekOrigin origin = SeekOrigin.Begin)
		{
			CheckDisposed();
			stream.Seek (offset, origin);
		}

		/// <summary>
		/// Reads a single byte from the stream.
		/// </summary>
		/// <returns>The byte read</returns>
		public byte ReadByte()
		{
			ReadInternal(buffer, 1);
			return buffer[0];
		}

		/// <summary>
		/// Reads a single signed byte from the stream.
		/// </summary>
		/// <returns>The byte read</returns>
		public sbyte ReadSByte()
		{
			ReadInternal(buffer, 1);
			return unchecked((sbyte)buffer[0]);
		}

		/// <summary>
		/// Reads a boolean from the stream. 1 byte is read.
		/// </summary>
		/// <returns>The boolean read</returns>
		public bool ReadBoolean()
		{
			ReadInternal(buffer, 1);
			return bitConverter.ToBoolean(buffer, 0);
		}

		/// <summary>
		/// Reads a 16-bit signed integer from the stream, using the bit converter
		/// for this reader. 2 bytes are read.
		/// </summary>
		/// <returns>The 16-bit integer read</returns>
		public short ReadInt16()
		{
			ReadInternal(buffer, 2);
			return bitConverter.ToInt16(buffer, 0);
		}

		/// <summary>
		/// Reads a 32-bit signed integer from the stream, using the bit converter
		/// for this reader. 4 bytes are read.
		/// </summary>
		/// <returns>The 32-bit integer read</returns>
		public int ReadInt32()
		{
			ReadInternal(buffer, 4);
			return bitConverter.ToInt32(buffer, 0);
		}

		/// <summary>
		/// Reads a 64-bit signed integer from the stream, using the bit converter
		/// for this reader. 8 bytes are read.
		/// </summary>
		/// <returns>The 64-bit integer read</returns>
		public long ReadInt64()
		{
			ReadInternal(buffer, 8);
			return bitConverter.ToInt64(buffer, 0);
		}

		/// <summary>
		/// Reads a 16-bit unsigned integer from the stream, using the bit converter
		/// for this reader. 2 bytes are read.
		/// </summary>
		/// <returns>The 16-bit unsigned integer read</returns>
		public ushort ReadUInt16()
		{
			ReadInternal(buffer, 2);
			return bitConverter.ToUInt16(buffer, 0);
		}

		/// <summary>
		/// Reads a 32-bit unsigned integer from the stream, using the bit converter
		/// for this reader. 4 bytes are read.
		/// </summary>
		/// <returns>The 32-bit unsigned integer read</returns>
		public uint ReadUInt32()
		{
			ReadInternal(buffer, 4);
			return bitConverter.ToUInt32(buffer, 0);
		}

		/// <summary>
		/// Reads a 64-bit unsigned integer from the stream, using the bit converter
		/// for this reader. 8 bytes are read.
		/// </summary>
		/// <returns>The 64-bit unsigned integer read</returns>
		public ulong ReadUInt64()
		{
			ReadInternal(buffer, 8);
			return bitConverter.ToUInt64(buffer, 0);
		}

		/// <summary>
		/// Reads a single-precision floating-point value from the stream, using the bit converter
		/// for this reader. 4 bytes are read.
		/// </summary>
		/// <returns>The floating point value read</returns>
		public float ReadSingle()
		{
			ReadInternal(buffer, 4);
			return bitConverter.ToSingle(buffer, 0);
		}

		/// <summary>
		/// Reads a double-precision floating-point value from the stream, using the bit converter
		/// for this reader. 8 bytes are read.
		/// </summary>
		/// <returns>The floating point value read</returns>
		public double ReadDouble()
		{
			ReadInternal(buffer, 8);
			return bitConverter.ToDouble(buffer, 0);
		}

		/// <summary>
		/// Reads a decimal value from the stream, using the bit converter
		/// for this reader. 16 bytes are read.
		/// </summary>
		/// <returns>The decimal value read</returns>
		public decimal ReadDecimal()
		{
			ReadInternal(buffer, 16);
			return bitConverter.ToDecimal(buffer, 0);
		}

		/// <summary>
		/// Reads a single character from the stream, using the character encoding for
		/// this reader. If no characters have been fully read by the time the stream ends,
		/// -1 is returned.
		/// </summary>
		/// <returns>The character read, or -1 for end of stream.</returns>
		public int Read()
		{
			int charsRead = Read(charBuffer, 0, 1);
			if (charsRead==0)
			{
				return -1;
			}
			else
			{
				return charBuffer[0];
			}
		}

		/// <summary>
		/// Reads the specified number of characters into the given buffer, starting at
		/// the given index.
		/// </summary>
		/// <param name="data">The buffer to copy data into</param>
		/// <param name="index">The first index to copy data into</param>
		/// <param name="count">The number of characters to read</param>
		/// <returns>The number of characters actually read. This will only be less than
		/// the requested number of characters if the end of the stream is reached.
		/// </returns>
		public int Read(char[] data, int index, int count)
		{
			CheckDisposed();
			if (buffer==null)
			{
				throw new ArgumentNullException("buffer");
			}
			if (index < 0)
			{
				throw new ArgumentOutOfRangeException("index");
			}
			if (count < 0)
			{
				throw new ArgumentOutOfRangeException("index");
			}
			if (count+index > data.Length)
			{
				throw new ArgumentException
					("Not enough space in buffer for specified number of characters starting at specified index");
			}

			int read=0;
			bool firstTime=true;

			// Use the normal buffer if we're only reading a small amount, otherwise
			// use at most 4K at a time.
			byte[] byteBuffer = buffer;

			if (byteBuffer.Length < count*minBytesPerChar)
			{
				byteBuffer = new byte[4096];
			}

			while (read < count)
			{
				int amountToRead;
				// First time through we know we haven't previously read any data
				if (firstTime)
				{
					amountToRead = count*minBytesPerChar;
					firstTime=false;
				}
				// After that we can only assume we need to fully read "chars left -1" characters
				// and a single byte of the character we may be in the middle of
				else
				{
					amountToRead = ((count-read-1)*minBytesPerChar)+1;
				}
				if (amountToRead > byteBuffer.Length)
				{
					amountToRead = byteBuffer.Length;
				}
				int bytesRead = TryReadInternal(byteBuffer, amountToRead);
				if (bytesRead==0)
				{
					return read;
				}
				int decoded = decoder.GetChars(byteBuffer, 0, bytesRead, data, index);
				read += decoded;
				index += decoded;
			}
			return read;
		}

		/// <summary>
		/// Reads the specified number of bytes into the given buffer, starting at
		/// the given index.
		/// </summary>
		/// <param name="buffer">The buffer to copy data into</param>
		/// <param name="index">The first index to copy data into</param>
		/// <param name="count">The number of bytes to read</param>
		/// <returns>The number of bytes actually read. This will only be less than
		/// the requested number of bytes if the end of the stream is reached.
		/// </returns>
		public int Read(byte[] buffer, int index, int count)
		{
			CheckDisposed();
			if (buffer==null)
			{
				throw new ArgumentNullException("buffer");
			}
			if (index < 0)
			{
				throw new ArgumentOutOfRangeException("index");
			}
			if (count < 0)
			{
				throw new ArgumentOutOfRangeException("index");
			}
			if (count+index > buffer.Length)
			{
				throw new ArgumentException
					("Not enough space in buffer for specified number of bytes starting at specified index");
			}
			int read=0;
			while (count > 0)
			{
				int block = stream.Read(buffer, index, count);
				if (block==0)
				{
					return read;
				}
				index += block;
				read += block;
				count -= block;
			}
			return read;
		}

		/// <summary>
		/// Reads the specified number of bytes, returning them in a new byte array.
		/// If not enough bytes are available before the end of the stream, this
		/// method will return what is available.
		/// </summary>
		/// <param name="count">The number of bytes to read</param>
		/// <returns>The bytes read</returns>
		public byte[] ReadBytes(int count)
		{
			CheckDisposed();
			if (count < 0)
			{
				throw new ArgumentOutOfRangeException("count");
			}
			byte[] ret = new byte[count];
			int index=0;
			while (index < count)
			{
				int read = stream.Read(ret, index, count-index);
				// Stream has finished half way through. That's fine, return what we've got.
				if (read==0)
				{
					byte[] copy = new byte[index];
					Buffer.BlockCopy(ret, 0, copy, 0, index);
					return copy;
				}
				index += read;
			}
			return ret;
		}

		/// <summary>
		/// Reads the specified number of bytes, returning them in a new byte array.
		/// If not enough bytes are available before the end of the stream, this
		/// method will throw an IOException.
		/// </summary>
		/// <param name="count">The number of bytes to read</param>
		/// <returns>The bytes read</returns>
		public byte[] ReadBytesOrThrow(int count)
		{
			byte[] ret = new byte[count];
			ReadInternal(ret, count);
			return ret;
		}

		/// <summary>
		/// Reads a 7-bit encoded integer from the stream. This is stored with the least significant
		/// information first, with 7 bits of information per byte of value, and the top
		/// bit as a continuation flag. This method is not affected by the endianness
		/// of the bit converter.
		/// </summary>
		/// <returns>The 7-bit encoded integer read from the stream.</returns>
		public int Read7BitEncodedInt()
		{
			CheckDisposed();

			int ret=0;
			for (int shift = 0; shift < 35; shift+=7)
			{
				int b = stream.ReadByte();
				if (b==-1)
				{
					throw new EndOfStreamException();
				}
				ret = ret | ((b&0x7f) << shift);
				if ((b & 0x80) == 0)
				{
					return ret;
				}
			}
			// Still haven't seen a byte with the high bit unset? Dodgy data.
			throw new IOException("Invalid 7-bit encoded integer in stream.");
		}

		/// <summary>
		/// Reads a 7-bit encoded integer from the stream. This is stored with the most significant
		/// information first, with 7 bits of information per byte of value, and the top
		/// bit as a continuation flag. This method is not affected by the endianness
		/// of the bit converter.
		/// </summary>
		/// <returns>The 7-bit encoded integer read from the stream.</returns>
		public int ReadBigEndian7BitEncodedInt()
		{
			CheckDisposed();

			int ret=0;
			for (int i=0; i < 5; i++)
			{
				int b = stream.ReadByte();
				if (b==-1)
				{
					throw new EndOfStreamException();
				}
				ret = (ret << 7) | (b&0x7f);
				if ((b & 0x80) == 0)
				{
					return ret;
				}
			}
			// Still haven't seen a byte with the high bit unset? Dodgy data.
			throw new IOException("Invalid 7-bit encoded integer in stream.");
		}

		/// <summary>
		/// Reads a length-prefixed string from the stream, using the encoding for this reader.
		/// A 7-bit encoded integer is first read, which specifies the number of bytes 
		/// to read from the stream. These bytes are then converted into a string with
		/// the encoding for this reader.
		/// </summary>
		/// <returns>The string read from the stream.</returns>
		public string ReadString()
		{
			int bytesToRead = Read7BitEncodedInt();

			byte[] data = new byte[bytesToRead];
			ReadInternal(data, bytesToRead);
			return encoding.GetString(data, 0, data.Length);
		}

		#endregion

		#region Private methods
		/// <summary>
		/// Checks whether or not the reader has been disposed, throwing an exception if so.
		/// </summary>
		void CheckDisposed()
		{
			if (disposed)
			{
				throw new ObjectDisposedException("EndianBinaryReader");
			}
		}

		/// <summary>
		/// Reads the given number of bytes from the stream, throwing an exception
		/// if they can't all be read.
		/// </summary>
		/// <param name="data">Buffer to read into</param>
		/// <param name="size">Number of bytes to read</param>
		void ReadInternal (byte[] data, int size)
		{
			CheckDisposed();
			int index=0;
			while (index < size)
			{
				int read = stream.Read(data, index, size-index);
				if (read==0)
				{
					throw new EndOfStreamException
						(String.Format("End of stream reached with {0} byte{1} left to read.", size-index,
						size-index==1 ? "s" : ""));
				}
				index += read;
			}
		}

		/// <summary>
		/// Reads the given number of bytes from the stream if possible, returning
		/// the number of bytes actually read, which may be less than requested if
		/// (and only if) the end of the stream is reached.
		/// </summary>
		/// <param name="data">Buffer to read into</param>
		/// <param name="size">Number of bytes to read</param>
		/// <returns>Number of bytes actually read</returns>
		int TryReadInternal (byte[] data, int size)
		{
			CheckDisposed();
			int index=0;
			while (index < size)
			{
				int read = stream.Read(data, index, size-index);
				if (read==0)
				{
					return index;
				}
				index += read;
			}
			return index;
		}
		#endregion

		#region IDisposable Members
		/// <summary>
		/// Disposes of the underlying stream.
		/// </summary>
		public void Dispose()
		{
			if (!disposed)
			{
				disposed = true;
				((IDisposable)stream).Dispose();
			}
		}
		#endregion
	}
}

namespace MiscUtil.Conversion
{
	/// <summary>
	/// Equivalent of System.BitConverter, but with either endianness.
	/// </summary>
	public abstract class EndianBitConverter
	{
		#region Endianness of this converter
		/// <summary>
		/// Indicates the byte order ("endianess") in which data is converted using this class.
		/// </summary>
		/// <remarks>
		/// Different computer architectures store data using different byte orders. "Big-endian"
		/// means the most significant byte is on the left end of a word. "Little-endian" means the 
		/// most significant byte is on the right end of a word.
		/// </remarks>
		/// <returns>true if this converter is little-endian, false otherwise.</returns>
		public abstract bool IsLittleEndian();

		/// <summary>
		/// Indicates the byte order ("endianess") in which data is converted using this class.
		/// </summary>
		public abstract Endianness Endianness { get; }
		#endregion

		#region Factory properties
		static LittleEndianBitConverter little = new LittleEndianBitConverter();
		/// <summary>
		/// Returns a little-endian bit converter instance. The same instance is
		/// always returned.
		/// </summary>
		public static LittleEndianBitConverter Little
		{
			get { return little; }
		}

		static BigEndianBitConverter big = new BigEndianBitConverter();
		/// <summary>
		/// Returns a big-endian bit converter instance. The same instance is
		/// always returned.
		/// </summary>
		public static BigEndianBitConverter Big
		{
			get { return big; }
		}
		#endregion

		#region Double/primitive conversions
		/// <summary>
		/// Converts the specified double-precision floating point number to a 
		/// 64-bit signed integer. Note: the endianness of this converter does not
		/// affect the returned value.
		/// </summary>
		/// <param name="value">The number to convert. </param>
		/// <returns>A 64-bit signed integer whose value is equivalent to value.</returns>
		public long DoubleToInt64Bits(double value)
		{
			return BitConverter.DoubleToInt64Bits(value);
		}

		/// <summary>
		/// Converts the specified 64-bit signed integer to a double-precision 
		/// floating point number. Note: the endianness of this converter does not
		/// affect the returned value.
		/// </summary>
		/// <param name="value">The number to convert. </param>
		/// <returns>A double-precision floating point number whose value is equivalent to value.</returns>
		public double Int64BitsToDouble (long value)
		{
			return BitConverter.Int64BitsToDouble(value);
		}

		/// <summary>
		/// Converts the specified single-precision floating point number to a 
		/// 32-bit signed integer. Note: the endianness of this converter does not
		/// affect the returned value.
		/// </summary>
		/// <param name="value">The number to convert. </param>
		/// <returns>A 32-bit signed integer whose value is equivalent to value.</returns>
		public int SingleToInt32Bits(float value)
		{
			return new Int32SingleUnion(value).AsInt32;
		}

		/// <summary>
		/// Converts the specified 32-bit signed integer to a single-precision floating point 
		/// number. Note: the endianness of this converter does not
		/// affect the returned value.
		/// </summary>
		/// <param name="value">The number to convert. </param>
		/// <returns>A single-precision floating point number whose value is equivalent to value.</returns>
		public float Int32BitsToSingle (int value)
		{
			return new Int32SingleUnion(value).AsSingle;
		}
		#endregion

		#region To(PrimitiveType) conversions
		/// <summary>
		/// Returns a Boolean value converted from one byte at a specified position in a byte array.
		/// </summary>
		/// <param name="value">An array of bytes.</param>
		/// <param name="startIndex">The starting position within value.</param>
		/// <returns>true if the byte at startIndex in value is nonzero; otherwise, false.</returns>
		public bool ToBoolean (byte[] value, int startIndex)
		{
			CheckByteArgument(value, startIndex, 1);
			return BitConverter.ToBoolean(value, startIndex);
		}

		/// <summary>
		/// Returns a Unicode character converted from two bytes at a specified position in a byte array.
		/// </summary>
		/// <param name="value">An array of bytes.</param>
		/// <param name="startIndex">The starting position within value.</param>
		/// <returns>A character formed by two bytes beginning at startIndex.</returns>
		public char ToChar (byte[] value, int startIndex)
		{
			return unchecked((char) (CheckedFromBytes(value, startIndex, 2)));
		}

		/// <summary>
		/// Returns a double-precision floating point number converted from eight bytes 
		/// at a specified position in a byte array.
		/// </summary>
		/// <param name="value">An array of bytes.</param>
		/// <param name="startIndex">The starting position within value.</param>
		/// <returns>A double precision floating point number formed by eight bytes beginning at startIndex.</returns>
		public double ToDouble (byte[] value, int startIndex)
		{
			return Int64BitsToDouble(ToInt64(value, startIndex));
		}

		/// <summary>
		/// Returns a single-precision floating point number converted from four bytes 
		/// at a specified position in a byte array.
		/// </summary>
		/// <param name="value">An array of bytes.</param>
		/// <param name="startIndex">The starting position within value.</param>
		/// <returns>A single precision floating point number formed by four bytes beginning at startIndex.</returns>
		public float ToSingle (byte[] value, int startIndex)
		{
			return Int32BitsToSingle(ToInt32(value, startIndex));
		}

		/// <summary>
		/// Returns a 16-bit signed integer converted from two bytes at a specified position in a byte array.
		/// </summary>
		/// <param name="value">An array of bytes.</param>
		/// <param name="startIndex">The starting position within value.</param>
		/// <returns>A 16-bit signed integer formed by two bytes beginning at startIndex.</returns>
		public short ToInt16 (byte[] value, int startIndex)
		{
			return unchecked((short) (CheckedFromBytes(value, startIndex, 2)));
		}

		/// <summary>
		/// Returns a 32-bit signed integer converted from four bytes at a specified position in a byte array.
		/// </summary>
		/// <param name="value">An array of bytes.</param>
		/// <param name="startIndex">The starting position within value.</param>
		/// <returns>A 32-bit signed integer formed by four bytes beginning at startIndex.</returns>
		public int ToInt32 (byte[] value, int startIndex)
		{
			return unchecked((int) (CheckedFromBytes(value, startIndex, 4)));
		}

		/// <summary>
		/// Returns a 64-bit signed integer converted from eight bytes at a specified position in a byte array.
		/// </summary>
		/// <param name="value">An array of bytes.</param>
		/// <param name="startIndex">The starting position within value.</param>
		/// <returns>A 64-bit signed integer formed by eight bytes beginning at startIndex.</returns>
		public long ToInt64 (byte[] value, int startIndex)
		{
			return CheckedFromBytes(value, startIndex, 8);
		}

		/// <summary>
		/// Returns a 16-bit unsigned integer converted from two bytes at a specified position in a byte array.
		/// </summary>
		/// <param name="value">An array of bytes.</param>
		/// <param name="startIndex">The starting position within value.</param>
		/// <returns>A 16-bit unsigned integer formed by two bytes beginning at startIndex.</returns>
		public ushort ToUInt16 (byte[] value, int startIndex)
		{
			return unchecked((ushort) (CheckedFromBytes(value, startIndex, 2)));
		}

		/// <summary>
		/// Returns a 32-bit unsigned integer converted from four bytes at a specified position in a byte array.
		/// </summary>
		/// <param name="value">An array of bytes.</param>
		/// <param name="startIndex">The starting position within value.</param>
		/// <returns>A 32-bit unsigned integer formed by four bytes beginning at startIndex.</returns>
		public uint ToUInt32 (byte[] value, int startIndex)
		{
			return unchecked((uint) (CheckedFromBytes(value, startIndex, 4)));
		}

		/// <summary>
		/// Returns a 64-bit unsigned integer converted from eight bytes at a specified position in a byte array.
		/// </summary>
		/// <param name="value">An array of bytes.</param>
		/// <param name="startIndex">The starting position within value.</param>
		/// <returns>A 64-bit unsigned integer formed by eight bytes beginning at startIndex.</returns>
		public ulong ToUInt64 (byte[] value, int startIndex)
		{
			return unchecked((ulong) (CheckedFromBytes(value, startIndex, 8)));
		}

		/// <summary>
		/// Checks the given argument for validity.
		/// </summary>
		/// <param name="value">The byte array passed in</param>
		/// <param name="startIndex">The start index passed in</param>
		/// <param name="bytesRequired">The number of bytes required</param>
		/// <exception cref="ArgumentNullException">value is a null reference</exception>
		/// <exception cref="ArgumentOutOfRangeException">
		/// startIndex is less than zero or greater than the length of value minus bytesRequired.
		/// </exception>
		static void CheckByteArgument(byte[] value, int startIndex, int bytesRequired)
		{
			if (value==null)
			{
				throw new ArgumentNullException("value");
			}
			if (startIndex < 0 || startIndex > value.Length-bytesRequired)
			{
				throw new ArgumentOutOfRangeException("startIndex");
			}
		}

        /// <summary>
        /// Checks the arguments for validity before calling FromBytes
        /// (which can therefore assume the arguments are valid).
        /// </summary>
        /// <param name="value">The bytes to convert after checking</param>
        /// <param name="startIndex">The index of the first byte to convert</param>
        /// <param name="bytesToConvert">The number of bytes to convert</param>
        /// <returns></returns>
		long CheckedFromBytes(byte[] value, int startIndex, int bytesToConvert)
		{
			CheckByteArgument(value, startIndex, bytesToConvert);
			return FromBytes(value, startIndex, bytesToConvert);
		}

		/// <summary>
		/// Convert the given number of bytes from the given array, from the given start
		/// position, into a long, using the bytes as the least significant part of the long.
		/// By the time this is called, the arguments have been checked for validity.
		/// </summary>
		/// <param name="value">The bytes to convert</param>
		/// <param name="startIndex">The index of the first byte to convert</param>
		/// <param name="bytesToConvert">The number of bytes to use in the conversion</param>
		/// <returns>The converted number</returns>
		protected abstract long FromBytes(byte[] value, int startIndex, int bytesToConvert);
		#endregion

		#region ToString conversions
		/// <summary>
		/// Returns a String converted from the elements of a byte array.
		/// </summary>
		/// <param name="value">An array of bytes.</param>
		/// <remarks>All the elements of value are converted.</remarks>
		/// <returns>
		/// A String of hexadecimal pairs separated by hyphens, where each pair 
		/// represents the corresponding element in value; for example, "7F-2C-4A".
		/// </returns>
		public static string ToString(byte[] value)
		{
			return BitConverter.ToString(value);
		}

		/// <summary>
		/// Returns a String converted from the elements of a byte array starting at a specified array position.
		/// </summary>
		/// <param name="value">An array of bytes.</param>
		/// <param name="startIndex">The starting position within value.</param>
		/// <remarks>The elements from array position startIndex to the end of the array are converted.</remarks>
		/// <returns>
		/// A String of hexadecimal pairs separated by hyphens, where each pair 
		/// represents the corresponding element in value; for example, "7F-2C-4A".
		/// </returns>
		public static string ToString(byte[] value, int startIndex)
		{
			return BitConverter.ToString(value, startIndex);
		}

		/// <summary>
		/// Returns a String converted from a specified number of bytes at a specified position in a byte array.
		/// </summary>
		/// <param name="value">An array of bytes.</param>
		/// <param name="startIndex">The starting position within value.</param>
		/// <param name="length">The number of bytes to convert.</param>
		/// <remarks>The length elements from array position startIndex are converted.</remarks>
		/// <returns>
		/// A String of hexadecimal pairs separated by hyphens, where each pair 
		/// represents the corresponding element in value; for example, "7F-2C-4A".
		/// </returns>
		public static string ToString(byte[] value, int startIndex, int length)
		{
			return BitConverter.ToString(value, startIndex, length);
		}
		#endregion

		#region	Decimal conversions
		/// <summary>
		/// Returns a decimal value converted from sixteen bytes 
		/// at a specified position in a byte array.
		/// </summary>
		/// <param name="value">An array of bytes.</param>
		/// <param name="startIndex">The starting position within value.</param>
		/// <returns>A decimal  formed by sixteen bytes beginning at startIndex.</returns>
		public decimal ToDecimal (byte[] value, int startIndex)
		{
			// HACK: This always assumes four parts, each in their own endianness,
			// starting with the first part at the start of the byte array.
			// On the other hand, there's no real format specified...
			int[] parts = new int[4];
			for (int i=0; i < 4; i++)
			{
				parts[i] = ToInt32(value, startIndex+i*4);
			}
			return new Decimal(parts);
		}

		/// <summary>
		/// Returns the specified decimal value as an array of bytes.
		/// </summary>
		/// <param name="value">The number to convert.</param>
		/// <returns>An array of bytes with length 16.</returns>
		public byte[] GetBytes(decimal value)
		{
			byte[] bytes = new byte[16];
			int[] parts = decimal.GetBits(value);
			for (int i=0; i < 4; i++)
			{
				CopyBytesImpl(parts[i], 4, bytes, i*4);
			}
			return bytes;
		}

		/// <summary>
		/// Copies the specified decimal value into the specified byte array,
		/// beginning at the specified index.
		/// </summary>
		/// <param name="value">A character to convert.</param>
		/// <param name="buffer">The byte array to copy the bytes into</param>
		/// <param name="index">The first index into the array to copy the bytes into</param>
		public void CopyBytes(decimal value, byte[] buffer, int index)
		{
			int[] parts = decimal.GetBits(value);
			for (int i=0; i < 4; i++)
			{
				CopyBytesImpl(parts[i], 4, buffer, i*4+index);
			}
		}
		#endregion

		#region GetBytes conversions
		/// <summary>
		/// Returns an array with the given number of bytes formed
		/// from the least significant bytes of the specified value.
		/// This is used to implement the other GetBytes methods.
		/// </summary>
		/// <param name="value">The value to get bytes for</param>
		/// <param name="bytes">The number of significant bytes to return</param>
		byte[] GetBytes(long value, int bytes)
		{
			byte[] buffer = new byte[bytes];
			CopyBytes(value, bytes, buffer, 0);
			return buffer;
		}

		/// <summary>
		/// Returns the specified Boolean value as an array of bytes.
		/// </summary>
		/// <param name="value">A Boolean value.</param>
		/// <returns>An array of bytes with length 1.</returns>
		public byte[] GetBytes(bool value)
		{
			return BitConverter.GetBytes(value);
		}

		/// <summary>
		/// Returns the specified Unicode character value as an array of bytes.
		/// </summary>
		/// <param name="value">A character to convert.</param>
		/// <returns>An array of bytes with length 2.</returns>
		public byte[] GetBytes(char value)
		{
			return GetBytes(value, 2);
		}

		/// <summary>
		/// Returns the specified double-precision floating point value as an array of bytes.
		/// </summary>
		/// <param name="value">The number to convert.</param>
		/// <returns>An array of bytes with length 8.</returns>
		public byte[] GetBytes(double value)
		{
			return GetBytes(DoubleToInt64Bits(value), 8);
		}
		
		/// <summary>
		/// Returns the specified 16-bit signed integer value as an array of bytes.
		/// </summary>
		/// <param name="value">The number to convert.</param>
		/// <returns>An array of bytes with length 2.</returns>
		public byte[] GetBytes(short value)
		{
			return GetBytes(value, 2);
		}

		/// <summary>
		/// Returns the specified 32-bit signed integer value as an array of bytes.
		/// </summary>
		/// <param name="value">The number to convert.</param>
		/// <returns>An array of bytes with length 4.</returns>
		public byte[] GetBytes(int value)
		{
			return GetBytes(value, 4);
		}

		/// <summary>
		/// Returns the specified 64-bit signed integer value as an array of bytes.
		/// </summary>
		/// <param name="value">The number to convert.</param>
		/// <returns>An array of bytes with length 8.</returns>
		public byte[] GetBytes(long value)
		{
			return GetBytes(value, 8);
		}

		/// <summary>
		/// Returns the specified single-precision floating point value as an array of bytes.
		/// </summary>
		/// <param name="value">The number to convert.</param>
		/// <returns>An array of bytes with length 4.</returns>
		public byte[] GetBytes(float value)
		{
			return GetBytes(SingleToInt32Bits(value), 4);
		}

		/// <summary>
		/// Returns the specified 16-bit unsigned integer value as an array of bytes.
		/// </summary>
		/// <param name="value">The number to convert.</param>
		/// <returns>An array of bytes with length 2.</returns>
		public byte[] GetBytes(ushort value)
		{
			return GetBytes(value, 2);
		}

		/// <summary>
		/// Returns the specified 32-bit unsigned integer value as an array of bytes.
		/// </summary>
		/// <param name="value">The number to convert.</param>
		/// <returns>An array of bytes with length 4.</returns>
		public byte[] GetBytes(uint value)
		{
			return GetBytes(value, 4);
		}

		/// <summary>
		/// Returns the specified 64-bit unsigned integer value as an array of bytes.
		/// </summary>
		/// <param name="value">The number to convert.</param>
		/// <returns>An array of bytes with length 8.</returns>
		public byte[] GetBytes(ulong value)
		{
			return GetBytes(unchecked((long)value), 8);
		}

		#endregion

		#region CopyBytes conversions
		/// <summary>
		/// Copies the given number of bytes from the least-specific
		/// end of the specified value into the specified byte array, beginning
		/// at the specified index.
		/// This is used to implement the other CopyBytes methods.
		/// </summary>
		/// <param name="value">The value to copy bytes for</param>
		/// <param name="bytes">The number of significant bytes to copy</param>
		/// <param name="buffer">The byte array to copy the bytes into</param>
		/// <param name="index">The first index into the array to copy the bytes into</param>
		void CopyBytes(long value, int bytes, byte[] buffer, int index)
		{
			if (buffer==null)
			{
				throw new ArgumentNullException("buffer", "Byte array must not be null");
			}
			if (buffer.Length < index+bytes)
			{
				throw new ArgumentOutOfRangeException("Buffer not big enough for value");
			}
			CopyBytesImpl(value, bytes, buffer, index);
		}

		/// <summary>
		/// Copies the given number of bytes from the least-specific
		/// end of the specified value into the specified byte array, beginning
		/// at the specified index.
		/// This must be implemented in concrete derived classes, but the implementation
		/// may assume that the value will fit into the buffer.
		/// </summary>
		/// <param name="value">The value to copy bytes for</param>
		/// <param name="bytes">The number of significant bytes to copy</param>
		/// <param name="buffer">The byte array to copy the bytes into</param>
		/// <param name="index">The first index into the array to copy the bytes into</param>
		protected abstract void CopyBytesImpl(long value, int bytes, byte[] buffer, int index);

		/// <summary>
		/// Copies the specified Boolean value into the specified byte array,
		/// beginning at the specified index.
		/// </summary>
		/// <param name="value">A Boolean value.</param>
		/// <param name="buffer">The byte array to copy the bytes into</param>
		/// <param name="index">The first index into the array to copy the bytes into</param>
		public void CopyBytes(bool value, byte[] buffer, int index)
		{
			CopyBytes(value ? 1 : 0, 1, buffer, index);
		}

		/// <summary>
		/// Copies the specified Unicode character value into the specified byte array,
		/// beginning at the specified index.
		/// </summary>
		/// <param name="value">A character to convert.</param>
		/// <param name="buffer">The byte array to copy the bytes into</param>
		/// <param name="index">The first index into the array to copy the bytes into</param>
		public void CopyBytes(char value, byte[] buffer, int index)
		{
			CopyBytes(value, 2, buffer, index);
		}

		/// <summary>
		/// Copies the specified double-precision floating point value into the specified byte array,
		/// beginning at the specified index.
		/// </summary>
		/// <param name="value">The number to convert.</param>
		/// <param name="buffer">The byte array to copy the bytes into</param>
		/// <param name="index">The first index into the array to copy the bytes into</param>
		public void CopyBytes(double value, byte[] buffer, int index)
		{
			CopyBytes(DoubleToInt64Bits(value), 8, buffer, index);
		}
		
		/// <summary>
		/// Copies the specified 16-bit signed integer value into the specified byte array,
		/// beginning at the specified index.
		/// </summary>
		/// <param name="value">The number to convert.</param>
		/// <param name="buffer">The byte array to copy the bytes into</param>
		/// <param name="index">The first index into the array to copy the bytes into</param>
		public void CopyBytes(short value, byte[] buffer, int index)
		{
			CopyBytes(value, 2, buffer, index);
		}

		/// <summary>
		/// Copies the specified 32-bit signed integer value into the specified byte array,
		/// beginning at the specified index.
		/// </summary>
		/// <param name="value">The number to convert.</param>
		/// <param name="buffer">The byte array to copy the bytes into</param>
		/// <param name="index">The first index into the array to copy the bytes into</param>
		public void CopyBytes(int value, byte[] buffer, int index)
		{
			CopyBytes(value, 4, buffer, index);
		}

		/// <summary>
		/// Copies the specified 64-bit signed integer value into the specified byte array,
		/// beginning at the specified index.
		/// </summary>
		/// <param name="value">The number to convert.</param>
		/// <param name="buffer">The byte array to copy the bytes into</param>
		/// <param name="index">The first index into the array to copy the bytes into</param>
		public void CopyBytes(long value, byte[] buffer, int index)
		{
			CopyBytes(value, 8, buffer, index);
		}

		/// <summary>
		/// Copies the specified single-precision floating point value into the specified byte array,
		/// beginning at the specified index.
		/// </summary>
		/// <param name="value">The number to convert.</param>
		/// <param name="buffer">The byte array to copy the bytes into</param>
		/// <param name="index">The first index into the array to copy the bytes into</param>
		public void CopyBytes(float value, byte[] buffer, int index)
		{
			CopyBytes(SingleToInt32Bits(value), 4, buffer, index);
		}

		/// <summary>
		/// Copies the specified 16-bit unsigned integer value into the specified byte array,
		/// beginning at the specified index.
		/// </summary>
		/// <param name="value">The number to convert.</param>
		/// <param name="buffer">The byte array to copy the bytes into</param>
		/// <param name="index">The first index into the array to copy the bytes into</param>
		public void CopyBytes(ushort value, byte[] buffer, int index)
		{
			CopyBytes(value, 2, buffer, index);
		}

		/// <summary>
		/// Copies the specified 32-bit unsigned integer value into the specified byte array,
		/// beginning at the specified index.
		/// </summary>
		/// <param name="value">The number to convert.</param>
		/// <param name="buffer">The byte array to copy the bytes into</param>
		/// <param name="index">The first index into the array to copy the bytes into</param>
		public void CopyBytes(uint value, byte[] buffer, int index)
		{
			CopyBytes(value, 4, buffer, index);
		}

		/// <summary>
		/// Copies the specified 64-bit unsigned integer value into the specified byte array,
		/// beginning at the specified index.
		/// </summary>
		/// <param name="value">The number to convert.</param>
		/// <param name="buffer">The byte array to copy the bytes into</param>
		/// <param name="index">The first index into the array to copy the bytes into</param>
		public void CopyBytes(ulong value, byte[] buffer, int index)
		{
			CopyBytes(unchecked((long)value), 8, buffer, index);
		}

		#endregion

		#region Private struct used for Single/Int32 conversions
		/// <summary>
		/// Union used solely for the equivalent of DoubleToInt64Bits and vice versa.
		/// </summary>
		[StructLayout(LayoutKind.Explicit)]
			struct Int32SingleUnion
		{
			/// <summary>
			/// Int32 version of the value.
			/// </summary>
			[FieldOffset(0)]
			int i;
			/// <summary>
			/// Single version of the value.
			/// </summary>
			[FieldOffset(0)]
			float f;

			/// <summary>
			/// Creates an instance representing the given integer.
			/// </summary>
			/// <param name="i">The integer value of the new instance.</param>
			internal Int32SingleUnion(int i)
			{
				this.f = 0; // Just to keep the compiler happy
				this.i = i;
			}

			/// <summary>
			/// Creates an instance representing the given floating point number.
			/// </summary>
			/// <param name="f">The floating point value of the new instance.</param>
			internal Int32SingleUnion(float f)
			{
				this.i = 0; // Just to keep the compiler happy
				this.f = f;
			}

			/// <summary>
			/// Returns the value of the instance as an integer.
			/// </summary>
			internal int AsInt32
			{
				get { return i; }
			}

			/// <summary>
			/// Returns the value of the instance as a floating point number.
			/// </summary>
			internal float AsSingle
			{
				get { return f; }
			}
		}
		#endregion
	}

	/// <summary>
	/// Endianness of a converter
	/// </summary>
	public enum Endianness
	{
		/// <summary>
		/// Little endian - least significant byte first
		/// </summary>
		LittleEndian,
		/// <summary>
		/// Big endian - most significant byte first
		/// </summary>
		BigEndian
	}

		/// <summary>
	/// Implementation of EndianBitConverter which converts to/from little-endian
	/// byte arrays.
	/// </summary>
	public sealed class LittleEndianBitConverter : EndianBitConverter
	{
		/// <summary>
		/// Indicates the byte order ("endianess") in which data is converted using this class.
		/// </summary>
		/// <remarks>
		/// Different computer architectures store data using different byte orders. "Big-endian"
		/// means the most significant byte is on the left end of a word. "Little-endian" means the 
		/// most significant byte is on the right end of a word.
		/// </remarks>
		/// <returns>true if this converter is little-endian, false otherwise.</returns>
		public sealed override bool IsLittleEndian()
		{
			return true;
		}

		/// <summary>
		/// Indicates the byte order ("endianess") in which data is converted using this class.
		/// </summary>
		public sealed override Endianness Endianness 
		{ 
			get { return Endianness.LittleEndian; }
		}

		/// <summary>
		/// Copies the specified number of bytes from value to buffer, starting at index.
		/// </summary>
		/// <param name="value">The value to copy</param>
		/// <param name="bytes">The number of bytes to copy</param>
		/// <param name="buffer">The buffer to copy the bytes into</param>
		/// <param name="index">The index to start at</param>
		protected override void CopyBytesImpl(long value, int bytes, byte[] buffer, int index)
		{
			for (int i=0; i < bytes; i++)
			{
				buffer[i+index] = unchecked((byte)(value&0xff));
				value = value >> 8;
			}
		}
		
		/// <summary>
		/// Returns a value built from the specified number of bytes from the given buffer,
		/// starting at index.
		/// </summary>
		/// <param name="buffer">The data in byte array format</param>
		/// <param name="startIndex">The first index to use</param>
		/// <param name="bytesToConvert">The number of bytes to use</param>
		/// <returns>The value built from the given bytes</returns>
		protected override long FromBytes(byte[] buffer, int startIndex, int bytesToConvert)
		{
			long ret = 0;
			for (int i=0; i < bytesToConvert; i++)
			{
				ret = unchecked((ret << 8) | buffer[startIndex+bytesToConvert-1-i]);
			}
			return ret;
		}
	}

	/// <summary>
	/// Implementation of EndianBitConverter which converts to/from big-endian
	/// byte arrays.
	/// </summary>
	public sealed class BigEndianBitConverter : EndianBitConverter
	{
		/// <summary>
		/// Indicates the byte order ("endianess") in which data is converted using this class.
		/// </summary>
		/// <remarks>
		/// Different computer architectures store data using different byte orders. "Big-endian"
		/// means the most significant byte is on the left end of a word. "Little-endian" means the 
		/// most significant byte is on the right end of a word.
		/// </remarks>
		/// <returns>true if this converter is little-endian, false otherwise.</returns>
		public sealed override bool IsLittleEndian()
		{
			return false;
		}

		/// <summary>
		/// Indicates the byte order ("endianess") in which data is converted using this class.
		/// </summary>
		public sealed override Endianness Endianness 
		{ 
			get { return Endianness.BigEndian; }
		}

		/// <summary>
		/// Copies the specified number of bytes from value to buffer, starting at index.
		/// </summary>
		/// <param name="value">The value to copy</param>
		/// <param name="bytes">The number of bytes to copy</param>
		/// <param name="buffer">The buffer to copy the bytes into</param>
		/// <param name="index">The index to start at</param>
		protected override void CopyBytesImpl(long value, int bytes, byte[] buffer, int index)
		{
			int endOffset = index+bytes-1;
			for (int i=0; i < bytes; i++)
			{
				buffer[endOffset-i] = unchecked((byte)(value&0xff));
				value = value >> 8;
			}
		}
		
		/// <summary>
		/// Returns a value built from the specified number of bytes from the given buffer,
		/// starting at index.
		/// </summary>
		/// <param name="buffer">The data in byte array format</param>
		/// <param name="startIndex">The first index to use</param>
		/// <param name="bytesToConvert">The number of bytes to use</param>
		/// <returns>The value built from the given bytes</returns>
		protected override long FromBytes(byte[] buffer, int startIndex, int bytesToConvert)
		{
			long ret = 0;
			for (int i=0; i < bytesToConvert; i++)
			{
				ret = unchecked((ret << 8) | buffer[startIndex+i]);
			}
			return ret;
		}
	}
}

