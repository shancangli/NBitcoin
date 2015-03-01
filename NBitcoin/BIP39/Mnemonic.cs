﻿
using System;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NBitcoin.Crypto;
#if !USEBC
using System.Security.Cryptography;
#endif
using NBitcoin.BouncyCastle.Security;
using NBitcoin.BouncyCastle.Crypto.Parameters;

namespace NBitcoin
{
	/// <summary>
	/// A .NET implementation of the Bitcoin Improvement Proposal - 39 (BIP39)
	/// BIP39 specification used as reference located here: https://github.com/bitcoin/bips/blob/master/bip-0039.mediawiki
	/// Made by thashiznets@yahoo.com.au
	/// v1.0.1.1
	/// I ♥ Bitcoin :)
	/// Bitcoin:1ETQjMkR1NNh4jwLuN5LxY7bMsHC9PUPSV
	/// </summary>
	public class Mnemonic
	{
		class BitWriter
		{
			List<bool> values = new List<bool>();
			public void Write(bool value)
			{
				values.Add(value);
			}

			internal void Write(byte[] bytes)
			{
				Write(bytes, bytes.Length * 8);
			}

			public void Write(byte[] bytes, int bitCount)
			{
				bytes = SwapEndianBytes(bytes);
				BitArray array = new BitArray(bytes);
				values.AddRange(array.OfType<bool>().Take(bitCount));
			}

			public byte[] ToBytes()
			{
				var array = ToBitArray();
				var bytes = ToByteArray(array);
				bytes = SwapEndianBytes(bytes);
				return bytes;
			}

			//BitArray.CopyTo do not exist in portable lib
			static byte[] ToByteArray(BitArray bits)
			{
				int arrayLength = bits.Length / 8;
				byte[] array = new byte[arrayLength];

				for(int i = 0 ; i < bits.Length ; i++)
				{
					int b = i / 8;
					int offset = i % 8;
					array[b] |= bits.Get(i) ? (byte)(1 << offset) : (byte)0;
				}
				return array;
			}


			public void Write(int[] indices)
			{
				Write(indices, indices.Length * 11);
			}
			public void Write(int[] indices, int bitCount)
			{
				foreach(var i in indices)
				{
					for(int p = 0 ; p < 11 ; p++)
					{
						if(bitCount <= 0)
							return;
						var v = (i & (1 << (10 - p))) != 0;
						Write(v);
						bitCount--;
					}
				}
			}

			public BitArray ToBitArray()
			{
				return new BitArray(values.ToArray());
			}

			public int[] ToIntegers()
			{
				return
					values
					.Select((v, i) => new
					{
						Group = i / 11,
						Value = v ? 1 << (10 - (i % 11)) : 0
					})
					.GroupBy(_ => _.Group, _ => _.Value)
					.Select(g => g.Sum())
					.ToArray();
			}


			static byte[] SwapEndianBytes(byte[] bytes)
			{
				byte[] output = new byte[bytes.Length];

				int index = 0;

				foreach(byte b in bytes)
				{
					byte[] ba = { b };
					BitArray bits = new BitArray(ba);

					int newByte = 0;
					if(bits.Get(7))
						newByte++;
					if(bits.Get(6))
						newByte += 2;
					if(bits.Get(5))
						newByte += 4;
					if(bits.Get(4))
						newByte += 8;
					if(bits.Get(3))
						newByte += 16;
					if(bits.Get(2))
						newByte += 32;
					if(bits.Get(1))
						newByte += 64;
					if(bits.Get(0))
						newByte += 128;

					output[index] = Convert.ToByte(newByte);

					index++;
				}

				//I love lamp
				return output;
			}


		}


		public Mnemonic(string mnemonic, Wordlist wordlist = null)
		{
			if(mnemonic == null)
				throw new ArgumentNullException("mnemonic");
			_Mnemonic = mnemonic.Trim();

			if(wordlist == null)
				wordlist = Wordlist.AutoDetect(mnemonic) ?? Wordlist.English;

			var words = mnemonic.Split(new char[] { ' ', '　' }, StringSplitOptions.RemoveEmptyEntries);
			//if the sentence is not at least 12 characters or cleanly divisible by 3, it is bad!
			if(!CorrectWordCount(words.Length))
			{
				throw new FormatException("Word count should be equals to 12,15,18,21 or 24");
			}
			_Words = words;
			_WordList = wordlist;
			_Indices = wordlist.GetIndices(words);
		}

		/// <summary>
		/// Generate a mnemonic
		/// </summary>
		/// <param name="wordList"></param>
		/// <param name="entropy"></param>
		public Mnemonic(Wordlist wordList, byte[] entropy = null)
		{
			wordList = wordList ?? Wordlist.English;
			_WordList = wordList;
			if(entropy == null)
				entropy = RandomUtils.GetBytes(32);

			var i = Array.IndexOf(entArray, entropy.Length * 8);
			if(i == -1)
				throw new ArgumentException("The length for entropy should be : " + String.Join(",", entArray), "entropy");

			int entcs = entcsArray[i];
			int ent = entArray[i];
			int cs = csArray[i];
			byte[] checksum = Hashes.SHA256(entropy);
			BitWriter entcsResult = new BitWriter();

			entcsResult.Write(entropy);
			entcsResult.Write(checksum, cs);
			_Indices = entcsResult.ToIntegers();
			_Words = _WordList.GetWords(_Indices);
			_Mnemonic = _WordList.GetSentence(_Indices);
		}

		public Mnemonic(Wordlist wordList, WordCount wordCount)
			: this(wordList, GenerateEntropy(wordCount))
		{

		}

		private static byte[] GenerateEntropy(WordCount wordCount)
		{
			var ms = (int)wordCount;
			if(!CorrectWordCount(ms))
				throw new ArgumentException("Word count should be equal to 12,15,18,21 or 24", "wordCount");
			int i = Array.IndexOf(msArray, (int)wordCount);
			return RandomUtils.GetBytes(entArray[i] / 8);
		}

		static readonly int[] msArray = new[] { 12, 15, 18, 21, 24 };
		static readonly int[] entcsArray = new[] { 132, 165, 198, 231, 264 };
		static readonly int[] csArray = new[] { 4, 5, 6, 7, 8 };
		static readonly int[] entArray = new[] { 128, 160, 192, 224, 256 };

		bool? _IsValidChecksum;
		public bool IsValidChecksum
		{
			get
			{
				if(_IsValidChecksum == null)
				{
					int i = Array.IndexOf(msArray, _Indices.Length);
					int cs = csArray[i];
					int ent = entArray[i];

					BitWriter writer = new BitWriter();
					writer.Write(_Indices, ent);
					var entropy = writer.ToBytes();
					var checksum = Hashes.SHA256(entropy);

					writer = new BitWriter();
					writer.Write(entropy);
					writer.Write(checksum, cs);
					var expectedIndices = writer.ToIntegers();
					_IsValidChecksum = expectedIndices.SequenceEqual(_Indices);
				}
				return _IsValidChecksum.Value;
			}
		}

		//private IEnumerable<bool> ToBits(int value)
		//{
		//	return null;
		//}

		private static bool CorrectWordCount(int ms)
		{
			return msArray.Any(_ => _ == ms);
		}


		private int ToInt(BitArray bits)
		{
			if(bits.Length != 11)
			{
				throw new InvalidOperationException("should never happen, bug in nbitcoin");
			}

			int number = 0;
			int base2Divide = 1024; //it's all downhill from here...literally we halve this for each bit we move to.

			//literally picture this loop as going from the most significant bit across to the least in the 11 bits, dividing by 2 for each bit as per binary/base 2
			foreach(bool b in bits)
			{
				if(b)
				{
					number = number + base2Divide;
				}

				base2Divide = base2Divide / 2;
			}

			return number;
		}

		private readonly Wordlist _WordList;
		public Wordlist WordList
		{
			get
			{
				return _WordList;
			}
		}

		private readonly int[] _Indices;
		public int[] Indices
		{
			get
			{
				return _Indices;
			}
		}
		private readonly string[] _Words;
		public string[] Words
		{
			get
			{
				return _Words;
			}
		}

		public byte[] DeriveSeed(string passphrase = null)
		{
			passphrase = passphrase ?? "";
			var salt = Concat(UTF8Encoding.UTF8.GetBytes("mnemonic"), Normalize(passphrase));
			var bytes = Normalize(_Mnemonic);

#if !USEBC
			return Pbkdf2.ComputeDerivedKey(new HMACSHA512(bytes), salt, 2048, 64);
#else
			var mac = MacUtilities.GetMac("HMAC-SHA_512");
			mac.Init(new KeyParameter(bytes));
			return Pbkdf2.ComputeDerivedKey(mac, salt, 2048, 64);
#endif

		}

		internal static byte[] Normalize(string str)
		{
			return Encoding.UTF8.GetBytes(NormalizeString(str));
		}

		internal static string NormalizeString(string word)
		{
#if !NOSTRNORMALIZE
			return word.Normalize(NormalizationForm.FormKD);
#else
			return KDTable.NormalizeKD(word);
#endif
		}

		public ExtKey DeriveExtKey(string passphrase = null)
		{
			return new ExtKey(DeriveSeed(passphrase));
		}

		static Byte[] Concat(Byte[] source1, Byte[] source2)
		{
			//Most efficient way to merge two arrays this according to http://stackoverflow.com/questions/415291/best-way-to-combine-two-or-more-byte-arrays-in-c-sharp
			Byte[] buffer = new Byte[source1.Length + source2.Length];
			System.Buffer.BlockCopy(source1, 0, buffer, 0, source1.Length);
			System.Buffer.BlockCopy(source2, 0, buffer, source1.Length, source2.Length);

			return buffer;
		}


		string _Mnemonic;
		public override string ToString()
		{
			return _Mnemonic;
		}


	}
	public enum WordCount : int
	{
		Twelve = 12,
		Fifteen = 15,
		Eighteen = 18,
		TwentyOne = 21,
		TwentyFour = 24
	}
}