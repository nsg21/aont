using System;
using System.Text;
using System.IO;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace aont
{
    class Program
    {
        static string REVISION="$Revision: 1.11 $";

        static long parse_size(string s)
        {
            long res;
            if (long.TryParse(s, out res))
            {
                return res;
            }
            else
            {
                int i = "kmgtp".IndexOf(s[s.Length - 1].ToString().ToLower());
                if (i >= 0)
                {
                    float m;
                    if (float.TryParse(s.Substring(0, s.Length - 1), out m))
                    {
                        long p = 1024;
                        while (0 < i--) p *= 1024;
                        return (long)(p * m);
                    }
                }
            }
            throw new ApplicationException("not a valid size: " + s);
        }

        static void show_help()
        {
            int i = 1 + REVISION.IndexOf(' ');
            Console.WriteLine(
                "aont {0} -- All-Or-Nothing transform.",
                REVISION.Substring(i, REVISION.IndexOfAny(" $".ToCharArray(), i) - i));
            Console.WriteLine(@"
Applies a transformation to a file, which makes this file unintelligible.
This transformation is easy to reverse and bring the file back to its
original readable form, unless portion of the transformed file is missing
or corrupt.
In this case it is difficult to reconstruct any remaining portion of the
original file.

Can be used to split file into pieces which can be meaningfully reassembled
only when all come together.

Usage:
  aont [/t]  [/n:number] [/s:size] [inputfile] [outputfile|name-template]
  aont /r inputfiles outputfile
  aont /v inputfiles

/t -- apply all-or-nothing transformation to a file
/n:number -- split the output into <number> parts
/s:size   -- split output into parts, each of size <size>
/r -- restore file to its original form and write result to outputfile
/v -- display which files and in what order will be processed");

        }

        /// <summary>
        /// compare strings treating corresponding sequences of digits as their numeric values 
        /// </summary>
        /// <param name="s1">first string</param>
        /// <param name="s2">second string</param>
        /// <returns>negative if s1&lt;s2, positive if s1>s2, zero if s1=s2 </returns>
        static int filenamecompare(string s1, string s2)
        {
            int i = 0;
            int minl=s1.Length;
            if( s2.Length<minl ) minl=s2.Length;
            while(i<minl)
            {
                if (char.IsDigit(s1[i]) && char.IsDigit(s2[i]))
                {
                    // extract numeral parts, compare them and judge if different
                    // mind leading zeroes; i define "001" < "1"
                    int jnz = i; // location of first nonzero in any of the strings
                    int j1 = i;
                    int j2 = i;
                    while( jnz<s1.Length && jnz<s2.Length && '0'==s1[jnz] && '0'==s2[jnz] ) ++jnz;
                    for (j1 = i; j1 < s1.Length && char.IsDigit(s1[j1]); ++j1) ;
                    int n1 = Int32.Parse(s1.Substring(i, j1 - i));
                    for (j2 = i; j2 < s2.Length && char.IsDigit(s2[j2]); ++j2) ;
                    int n2 = Int32.Parse(s2.Substring(i, j2 - i));
                    if (n1 != n2) return n1 - n2;
                    if (j1 != j2)
                    {
                        // different length of numeric part due to leading zeroes, judge now
                        if (jnz >= s1.Length) return -1;
                        if (jnz >= s2.Length) return 1;
                        return s1[jnz]<s2[jnz]?-1:1;

                    }
                    // numerical parts are identical as strings, continue searrch as usual
                    i = j1; // or j2
                    continue;
                }
                else if (s1[i] < s2[i]) return -1;
                else if (s1[i] > s2[i]) return 1;
                ++i;
            }
            // at this point one string is prefix of the other
            return s1.Length - s2.Length;
        }

        static List<string> unfoldWildcard(string inname)
        {
            string dir = Path.GetDirectoryName(inname);
            if ("" == dir) dir = ".";
            if (dir.Contains("*")) throw new ApplicationException(String.Format("{0} has wildcard in directory name", inname));
            string name = Path.GetFileName(inname);
            List<string> wfiles= new List<string>(Directory.GetFiles(dir, name, SearchOption.TopDirectoryOnly));
            wfiles.Sort(filenamecompare);
            return wfiles;
        }

        static List<string> unfoldAllNames(List<string> names)
        {
            List<string> ret = new List<string>();
            foreach (string wild in names)
            {
                foreach (string n in unfoldWildcard(wild))
                {
                    ret.Add(n);
                }
            }
            return ret;
        }

        static void Main(string[] args)
        {
            List<string> files=new List<string>();
            string outfile = null;
            string infile = null;
            int mode = 1;
            int number = 0;
            long size = 0;

            if (0 == args.Length)
            {
                show_help();
                return;
            }
            foreach (string arg in args)
            {
                if ("/?" == arg) { show_help(); return; }
                else if ("/t" == arg) mode = 1;
                else if ("/r" == arg) mode = 2;
                else if ("/v" == arg) mode = 3;
                else if (2 <= arg.Length && "/n" == arg.Substring(0, 2)) number = int.Parse(arg.Substring(3));
                else if (2 <= arg.Length && "/s" == arg.Substring(0, 2)) size = parse_size(arg.Substring(3));
                else files.Add(arg);
            }
            Stream instream;
            Stream outstream;
            if (files.Count >= 1) infile = files[0];
            if (files.Count >= 2) outfile = files[1];
            try
            {
                switch (mode)
                {
                    case 1: // forward
                        if (null == infile) infile = "-";
                        if ("-" == infile)
                            instream = Console.OpenStandardInput();
                        else
                            instream = new FileStream(infile, FileMode.Open);
                        if( 0==size ) 
                           if( 0!=number )
                             {
                             // if size is not specified, but number is,
                             // split result into number approximately equal parts
                             // result lengths is original length +IV +hashed key
                             long l=instream.Length;
                             int b=aont.Encryption.BlockSize/8;
                             l+=b-l%b;
                             l+=aont.Hash.HashSize/8+b;
                             size=(l / number)+(0<l % number?1:0);
                             //Console.WriteLine("l={0}, size={1}",l,size);
                             }
                           else number=1;

                        if ((null == outfile && "-" == infile) || "-" == outfile) outstream = Console.OpenStandardOutput();
                        else if (null == outfile)
                            if(1==number) outstream = new FileStream(infile + ".aont",FileMode.Create, FileAccess.Write);
                            else outstream = new SplitStream(infile + ".part*", number, size);
                        else
                            if (1 == number) outstream=new FileStream(outfile, FileMode.Create, FileAccess.Write);
                            else outstream = new SplitStream(outfile, number, size);
                        aont.Transform(instream, outstream);
                        break;
                    case 2: //reverse
                        if( 0!=number || 0!=size ) throw new ApplicationException("/n and /s options may only be specified in transform mode");
                        if (null == infile) throw new ApplicationException("need filename");
                        if (files.Count >= 2)
                        {
                            outfile = files[files.Count - 1];
                            files.RemoveAt(files.Count - 1);
                            files = unfoldAllNames(files);
                        }
                        else
                        {
                            files = unfoldAllNames(files);
                            outfile = files[0].Substring(0, files[0].LastIndexOf(".part")) + ".restored";
                        }
                        try
                        {
                            aont.UnTransform(files, outfile);
                        }
                        catch (CryptographicException)
                        {
                            throw new ApplicationException(String.Format("Reverse transformation cannot be completed. Parts of file may be corrupt, missing or in wrong order."));
                        }
                        break;
                    case 3: // unfold wildcards
                        Console.WriteLine("The files will be processed in the following order:");
                        foreach (string f in unfoldAllNames(files))
                        {
                            Console.WriteLine(f);
                        }


                        break;
                }
            }
            catch (ApplicationException ae)
            {
                Console.Error.WriteLine("Error: {0}", ae.Message);
            }
            catch (IOException ie)
            {
                Console.Error.WriteLine("File error: {0}", ie.Message);
            }
        }
    }

    /// <summary>
    /// Generates write only stream that automatically splits contents into several numbered files
    /// </summary>
    class SplitStream : Stream
    {
        int number; // number of pieces
        long size; // requested piece size
        string filenametemplate;
        int filenumber;
        long currentsize; // number of bytes written to current chunk so far.
        FileStream fs=null;
        static char[] wild = new char[] { '*', '{' };
        public static bool has_wild(string filename)
          {
            return 0<=filename.IndexOfAny(wild);
          }
        public SplitStream(string filename, int num, long sz)
        {
            number=num;
            size=sz>0?sz:long.MaxValue;
            StringBuilder sb = new StringBuilder(filename);
            int pa = sb.ToString().IndexOf('*');
            if (0 <= pa)
            {
                sb.Remove(pa, 1);
                sb.Insert(pa, "{0}");
            }
            pa = sb.ToString().IndexOfAny(wild);
            int pl = sb.ToString().LastIndexOfAny(wild);
            if ((pa < 0 || pl > pa) && number>1) throw new ApplicationException(String.Format("Must have single wild character in filename template {0}", sb.ToString()));
            filenametemplate = sb.ToString();
            filenumber=0;
            currentsize=size;
            fs = null;
        }

        /// <summary>
        /// Make sure that current file still has room for data (according to /n and /s conditions).
        /// If not, open next.
        /// </summary>
        private void ensuredata()
        {
            if (currentsize >= size && (0 == number || filenumber < number))
            {
                if (null != fs) fs.Close();
                ++filenumber;
                fs = new FileStream(String.Format(filenametemplate, filenumber), FileMode.Create, FileAccess.Write);
                currentsize = 0;
            }
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            // int i;
            // for (i = 0; i < count; ++i) WriteByte(buffer[offset + i]);
            // 80 seconds to write iso with /n:15 /s:16

            while(count>0){
                int towrite;
                ensuredata();
                if ((size - currentsize) < count && (0 == number || filenumber < number)) towrite = (int)(size - currentsize);
                else towrite = count ;
                fs.Write(buffer, offset, towrite);
                currentsize += towrite;
                offset += towrite;
                count -= towrite;
            }
            // 51 seconds to write iso with /n:15 /s:16, 64k write buffer
        }

        public override void  WriteByte(byte value)
        {
            ensuredata();
            fs.WriteByte(value);
            ++currentsize;
            //base.WriteByte(value);
        }

        public override void Close()
        {
            if (null != fs) fs.Close();
            base.Close();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotImplementedException();
        }

        public override void SetLength(long value)
        {
            throw new NotImplementedException();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotImplementedException();
        }

        public override long Length
        {
            get { throw new NotImplementedException(); }
        }

        public override void Flush()
        {
            throw new NotImplementedException();
        }

        public override long Position
        {
            get
            {
                throw new NotImplementedException();
            }
            set
            {
                throw new NotImplementedException();
            }
        }

        public override bool CanRead
        {
            get { return false; }
        }

        public override bool CanSeek
        {
            get { return false; }
        }

        public override bool CanWrite
        {
            get { return true; }
        }
    }

    /// <summary>
    /// Generates write only stream that automatically splits contents into several numbered files
    /// </summary>
    class JoinStream : Stream
    {
        List<string> filenames=null;
        List<long> cumfilelength = null; // cumulative file length
        int filenumber; // currently open file
        FileStream fs = null;
        long pos; // current position
        public JoinStream(List<string> fns)
        {
            filenumber = 0;
            filenames = new List<string>(fns);
            cumfilelength=new List<long>(fns.Count);
            fs = null;
            long len = 0;
            pos = 0;
            foreach( string f in filenames ) {
                len+=(new FileInfo(f)).Length;
                cumfilelength.Add(len);
            }
            fs = new FileStream(filenames[0], FileMode.Open, FileAccess.Read);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotImplementedException();
        }

        public override void WriteByte(byte value)
        {
            throw new NotImplementedException();
        }

        public override void Close()
        {
            if (null != fs) fs.Close();
            base.Close();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            /*
             * straightforward implementation
             * timing: 98, 90
             
            for (int i = 0; i < count; ++i)
            {
                int r = ReadByte();
                if (-1 == r ) return i;
                buffer[offset+i] = (byte)r;
            }
            return count;
            */
            int read;
            int totalread=0;
            pos += count;
            while (count > 0)
            {
                read = fs.Read(buffer, offset, count);
                totalread += read;
                if (read < count)
                {
                    ++filenumber;
                    if (filenumber >= filenames.Count) return totalread;
                    fs = new FileStream(filenames[filenumber], FileMode.Open, FileAccess.Read);
                }
                count -= read;
                offset += read;
            }
            return totalread;
            /* buffered version timed at 63 seconds */
        }

        public override int ReadByte()
        {
            int r;
            do
            {
                r = fs.ReadByte();
                if (r < 0)
                {
                    ++filenumber;
                    if (filenumber >= filenames.Count) return -1;
                    fs = new FileStream(filenames[filenumber], FileMode.Open, FileAccess.Read);
                }
            } while (r < 0);
            ++pos;
            return r;
        }

        public override void SetLength(long value)
        {
            throw new NotImplementedException();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            if (SeekOrigin.Begin == origin)
            {
                // find which file index contains the sought offset
                for (var i = 0; i < cumfilelength.Count; ++i)
                {
                    if (offset < cumfilelength[i]) // i is the target file
                    {
                        if (i != filenumber)
                        {
                            fs.Close();
                            filenumber = i;
                            fs = new FileStream(filenames[i], FileMode.Open, FileAccess.Read);
                        }
                        long prevoffs = 0;
                        if (i > 0) prevoffs = cumfilelength[i - 1];
                        return pos = prevoffs + fs.Seek(offset - prevoffs, SeekOrigin.Begin);
                    }
                }
                throw new ApplicationException(String.Format("Seek past total files length {0}>={1}", offset, cumfilelength[cumfilelength.Count - 1]));
            }
            else throw new NotImplementedException();
        }

        public override long Length
        {
            get { return cumfilelength[cumfilelength.Count-1]; }
        }

        public override void Flush()
        {
            throw new NotImplementedException();
        }

        public override long Position
        {
            get
            {
                return pos;
            }
            set
            {
                throw new NotImplementedException();
            }
        }

        public override bool CanRead
        {
            get { return false; }
        }

        public override bool CanSeek
        {
            get { return false; }
        }

        public override bool CanWrite
        {
            get { return true; }
        }
    }

    // All-or-nothing transform
    class aont
    {
        static int BufSize = 0x10000;
        // common parameters use for forward and reverse transform
        public static SymmetricAlgorithm Encryption = new AesManaged();
        public static int KEYSIZE = 256;
        static CipherMode MODE=CipherMode.CBC;
        static PaddingMode PADDING = PaddingMode.ISO10126;

        public static HashAlgorithm Hash = new SHA256Managed();

        public static void Transform(string infilename, string outfilename)
        {
            // Make random key and setup other parameters
            Encryption.KeySize = KEYSIZE;
            Encryption.GenerateIV();
            Encryption.GenerateKey();
            Encryption.Mode = MODE;
            Encryption.Padding = PADDING;
            
            Stream file;
            if ("-" == infilename)
                file = Console.OpenStandardInput();
            else
                file = new FileStream(infilename, FileMode.Open);

            Stream outstream;
            if ("-" == outfilename)
                outstream = Console.OpenStandardOutput();
            else
                outstream = new FileStream(outfilename, FileMode.Create);
            Transform(file, outstream);

        }

        public static void Transform(Stream file, Stream outstream)
        {
            using ( Stream
                cs = new CryptoStream(file, Encryption.CreateEncryptor(), CryptoStreamMode.Read)
                )
            {
                byte[] buf = new byte[BufSize];
                int r;
                // Output IV
                // Encrypt each block with the random key
                // Calculate hash of IV and all encrypted blocks
                outstream.Write(Encryption.IV, 0, Encryption.IV.Length);
                Hash.TransformBlock(Encryption.IV, 0, Encryption.IV.Length, buf, 0);
                do
                {
                    r = cs.Read(buf, 0, buf.Length);
                    outstream.Write(buf, 0, r);
                    Hash.TransformBlock(buf, 0, r, buf, 0);
                } while (r == buf.Length);
                Hash.TransformFinalBlock(buf, 0, 0);
                // Console.WriteLine("hash={0}", BitConverter.ToString(h.Hash).Replace("-", ""));

                // Finally, emit the key xored with the hash
                // Hash size is chosen to match the key size, but if it does not
                // then use only beginning portion of it or repeat it as needed.
                byte[] xorpf = new byte[Encryption.KeySize / 8];
                int i = 0, j = 0;
                foreach (byte kb in Encryption.Key)
                {
                    xorpf[j] = (byte)(Hash.Hash[i] ^ kb);
                    ++i; ++j;
                    if (i >= Hash.Hash.Length) i -= Hash.Hash.Length;
                }
                outstream.Write(xorpf, 0, xorpf.Length);
            }
            outstream.Close();
        }

        public static void UnTransform(List<string> infilenames, string outfilename)
        {
            Stream infilestream;
            if (1 == infilenames.Count) infilestream = new FileStream(infilenames[0], FileMode.Open);
            else infilestream = new JoinStream(infilenames);
            if (outfilename.Contains(".part") && File.Exists(outfilename) ) throw new ApplicationException(String.Format("File {0} already exists, possibly missing output file name", outfilename));
            using (Stream outfilestream = 
                "-" == outfilename ? Console.OpenStandardOutput() : new FileStream(outfilename, FileMode.Create)
                )
            {
                UnTransform(infilestream, outfilestream);
            }

        }
        public static void UnTransform(Stream infilestream, Stream outfilestream)
        {
            Encryption.KeySize = KEYSIZE;
            Encryption.Mode = MODE;
            Encryption.Padding = PADDING;
            byte[] iv = new byte[Encryption.BlockSize / 8];
            
            byte[] storedkey = new byte[Encryption.KeySize / 8];
            long databytes = infilestream.Length - storedkey.Length;

            infilestream.Read(iv, 0, iv.Length);
            Encryption.IV = iv;
            //Console.WriteLine("iv={0}", BitConverter.ToString(c.IV).Replace("-", ""));


            byte[] buf = new byte[BufSize];
            int r;
            Hash.TransformBlock(Encryption.IV, 0, Encryption.IV.Length, buf, 0);
            do
            {
                long left = databytes - infilestream.Position;
                if (left > (long)buf.Length)
                    r = infilestream.Read(buf, 0, buf.Length);
                else
                    r = infilestream.Read(buf, 0, (int)left);
                Hash.TransformBlock(buf, 0, r, buf, 0);
            } while (infilestream.Position<databytes);
            Hash.TransformFinalBlock(iv, 0, 0);
            //Console.WriteLine("hash={0}", BitConverter.ToString(h.Hash).Replace("-", ""));
            r = infilestream.Read(storedkey, 0, storedkey.Length);
            //Console.WriteLine("last={0}", BitConverter.ToString(enchash).Replace("-", ""));
            if (r != storedkey.Length) throw new ApplicationException("cannot read final block");
            // bytes of key are readonly, must change as a whole
            int j = 0;
            for (int i = 0; i < Hash.Hash.Length; ++i)
            {
                storedkey[i] = (byte)(storedkey[i] ^ Hash.Hash[j]);
                ++j;
                if (j >= Hash.Hash.Length) j -= Hash.Hash.Length;
            }
            Encryption.Key = storedkey;
            //Console.WriteLine("key={0}", BitConverter.ToString(c.Key).Replace("-", ""));

            infilestream.Seek(Encryption.IV.Length, SeekOrigin.Begin);
            using (Stream cs = new CryptoStream(outfilestream, Encryption.CreateDecryptor(), CryptoStreamMode.Write))
            {
                while (infilestream.Position < databytes)
                {
                    long left = databytes - infilestream.Position;
                    if (left > (long)buf.Length)
                        r = infilestream.Read(buf, 0, buf.Length);
                    else
                        r = infilestream.Read(buf, 0, (int)left);
                    cs.Write(buf, 0, r);
                }
            }
        }
    }

}
