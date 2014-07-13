using System;
using System.Text;
using System.IO;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace aont
{
    public static class Util
    {
        /// <summary>
        /// Interpret string as a number with a scaling suffix and return its value.
        /// </summary>
        /// <param name="s">number with a suffix. Suffix is one of k, m, g, t, p or empty.</param>
        /// <returns></returns>
        public static long parse_size(string s)
        {
            long res;
            if (long.TryParse(s, out res)) return res;
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

        /// <summary>
        /// Compare strings, while treating corresponding sequences of digits by their numeric values 
        /// </summary>
        /// <param name="s1">first string</param>
        /// <param name="s2">second string</param>
        /// <returns>negative if s1&lt;s2, positive if s1>s2, zero if s1=s2 </returns>
        public static int filenamecompare(string s1, string s2)
        {
            int i = 0;
            int minl = s1.Length;
            if (s2.Length < minl) minl = s2.Length;
            while (i < minl)
            {
                if (char.IsDigit(s1[i]) && char.IsDigit(s2[i]))
                {
                    // Start of numeric subsequence
                    int jnz = i;
                    int j1 = i;
                    int j2 = i;
                    // Findlocation of first nonzero in any of the strings
                    // This may be needed further if numerical values are same
                    while (jnz < s1.Length && jnz < s2.Length && '0' == s1[jnz] && '0' == s2[jnz]) ++jnz;
                    // Extract nueric part from first arg
                    for (j1 = i; j1 < s1.Length && char.IsDigit(s1[j1]); ++j1) ;
                    int n1 = Int32.Parse(s1.Substring(i, j1 - i));
                    // Extract nueric part from second arg
                    for (j2 = i; j2 < s2.Length && char.IsDigit(s2[j2]); ++j2) ;
                    int n2 = Int32.Parse(s2.Substring(i, j2 - i));
                    if (n1 != n2) return n1 - n2;
                    if (j1 != j2)
                    {
                        // different length of numeric part with equal numeric value is due to leading zeroes
                        // I define "001" < "1", "00"<"0000"
                        if (jnz >= s1.Length) return -1;
                        if (jnz >= s2.Length) return 1;
                        return s1[jnz] < s2[jnz] ? -1 : 1;
                    }
                    // numerical parts are identical as strings, continue compare as usual
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

        public static void CopyStream(Stream input, Stream output)
        {
            byte[] buf = new byte[0x1000];
            int rd;
            while ((rd = input.Read(buf, 0, buf.Length)) > 0)
            {
                output.Write(buf, 0, rd);
            }
        }

    }

    class Program
    {
        static string REVISION="$Revision: 1.13 $";
        static void show_help()
        {
            int i = 1 + REVISION.IndexOf(' ');
            Console.WriteLine(
                "aont {0} -- All-Or-Nothing transform. (C) nsg, 2010",
                REVISION.Substring(i, REVISION.IndexOfAny(" $".ToCharArray(), i) - i));
            Console.WriteLine(@"
Applies a transformation to a file, which makes this file unintelligible.
This transformation is easy to reverse and bring the file back to its
original readable form, unless portion of the transformed file is missing
or corrupt.
In this case it is difficult to reconstruct any remaining portion of the
original file.

Usage:
  aont [/t] [/p /v] [/n:number] [/s:size] input-file output-file-name-template
  aont [/r] [/p /v] input-files output-file

/t -- apply all-or-nothing transformation to a file
/n:number -- split the output into <number> parts
/s:size   -- split output into parts, each of size <size>
/r -- restore file to its original form and write result to outputfile
/v -- verbose operation (for troubleshooting)
/p -- no transformation, split/merge plaintext");

        }

        static List<string> unfoldWildcard(string inname)
        {
            string dir = Path.GetDirectoryName(inname);
            if ("" == dir) dir = ".";
            if (dir.Contains("*")) throw new ApplicationException(String.Format("{0} has wildcard in directory name", inname));
            string name = Path.GetFileName(inname);
            if (name.Contains("*"))
            {
                List<string> wfiles = new List<string>(Directory.GetFiles(dir, name, SearchOption.TopDirectoryOnly));
                wfiles.Sort(Util.filenamecompare);
                return wfiles;
            }
            else
            {
                return new List<string>() { inname };
            }

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

        static string EXT1 = ".aont";
        static string EXTMULTI = ".part";
        static void Main(string[] args)
        {
            List<string> files=new List<string>();
            string outfile = null;
            string infile = null;
            int mode = 0;
            int number = 0;
            long size = 0;
            bool plain = false; // true when no transformation, split/merge only
            bool verbose = false;
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
                else if ("/v" == arg) verbose=true;
                else if ("/p"==arg ) plain=true;
                else if (2 <= arg.Length && "/n" == arg.Substring(0, 2)) number = int.Parse(arg.Substring(3));
                else if (2 <= arg.Length && "/s" == arg.Substring(0, 2)) size = Util.parse_size(arg.Substring(3));
                else files.Add(arg);
            }
            Stream instream;
            Stream outstream;
            if( 0==files.Count ) files.Add("-");
            if (files.Count >= 1) infile = files[0];
            if (files.Count >= 2) outfile = files[1];
            try
            {
                switch (mode)
                {
                    case 0:
                        // size or number option implies forward transformation
                        if (0 < number || 0 < size) goto case 1;
                        // wildacard input file pattern, or special extension -- reverse transform from parts
                        if (infile.Contains("*") || infile.Contains(EXT1) || infile.Contains(EXTMULTI)) goto case 2;
                        // if input file does not exist, guess reverse with implied input
                        if ("-" != infile && (!File.Exists(infile) && (File.Exists(infile+EXT1) || File.Exists(infile+EXTMULTI+"1")) ) && null == outfile) goto case 2;
                        // assume forward transform if no other hints
                        goto case 1;
                    case 1: // forward
                        if ("-" == infile)
                            instream = Console.OpenStandardInput();
                        else
                            instream = new FileStream(infile, FileMode.Open);
                        if( 0==size )
                           // if size is not specified, but number is,
                           // split result into number of approximately equal parts
                            if (0 != number) size = plain?instream.Length/number:aont.EstimateOutputPartLength(instream.Length, number);
                            else
                            {
                                // if neither is specified, use settings which I use most often
                                number = 2;
                                size = 128;
                            }

                        if (null == outfile)
                        {
                            if ("-" == infile) outfile = "-";
                            else if (1 == number) outfile = infile + EXT1;
                            else outfile = infile + EXTMULTI + "*";
                        }
                        if (verbose) Console.WriteLine("{0} file {1} into {2}", plain ? "write" : "transform", infile, outfile);
                        if ("-" == outfile) outstream = Console.OpenStandardOutput();
                        else if (1 == number) outstream = new FileStream(outfile, FileMode.Create, FileAccess.Write);
                        else outstream = new SplitStream(outfile, number, size, verbose);
                        if (plain) Util.CopyStream(instream, outstream);
                        else aont.Transform(instream, outstream);
                        break;
                    case 2: //reverse
                        if( 0!=number || 0!=size ) throw new ApplicationException("/n and /s options may only be specified in transform mode");
                        if ("-" == infile) throw new ApplicationException("Reverse transformation can only be applied to a file. Need filename.");
                        if ( !infile.Contains("*") && !File.Exists(infile) && null == outfile)
                        {
                            string aontext=infile+EXT1;
                            if (File.Exists(aontext)) files.Insert(0, aontext);
                            else files.Insert(0, infile + EXTMULTI+"*");
                        }
                        if (files.Count >= 2)
                        {
                            // last name in list is output file name
                            outfile = files[files.Count - 1];
                            files.RemoveAt(files.Count - 1);
                            files = unfoldAllNames(files);
                        }
                        else
                        {
                            // if there is only one name (possibly, template), try to guess output file name 
                            files = unfoldAllNames(files);
                            if( 0<files.Count) outfile = Path.ChangeExtension(files[0], ".restored");
                        }
                        try
                        {
                            Stream infilestream;
                            if (1 == files.Count)
                            {
                                if (verbose) Console.WriteLine("read {0}", files[0]);
                                infilestream = new FileStream(files[0], FileMode.Open);
                            }
                            else infilestream = new JoinStream(files, verbose);
                            if (outfile.Contains(".part") && File.Exists(outfile)) throw new ApplicationException(String.Format("File {0} already exists, possibly missing output file name", outfile));
                            using (Stream outfilestream =
                                "-" == outfile ? Console.OpenStandardOutput() : new FileStream(outfile, FileMode.Create)
                                )
                            {
                                if (plain) Util.CopyStream(infilestream, outfilestream);
                                else aont.UnTransform(infilestream, outfilestream);
                                if (verbose) Console.WriteLine("save {1} to {0}", outfile,plain?"plain":"reverse-transformed");
                            }

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
    public class SplitStream : Stream
    {
        int number; // number of pieces
        long size; // requested piece size
        string filenametemplate;
        int filenumber;
        long currentsize; // number of bytes written to current chunk so far.
        FileStream fs=null;
        static char[] wild = new char[] { '*', '{' };
        private bool verbose;
        public static bool has_wild(string filename)
          {
            return 0<=filename.IndexOfAny(wild);
          }
        public SplitStream(string filename, int num, long sz, bool verb=false)
        {
            verbose = verb;
            number=num;
            if (verbose) Console.WriteLine("split into {0} files, {1} bytes each", num, sz);
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
                string nm = String.Format(filenametemplate, filenumber);
                fs = new FileStream(nm, FileMode.Create, FileAccess.Write);
                if (verbose) Console.WriteLine("write {0}", nm);
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
    public class JoinStream : Stream
    {
        IList<string> filenames=null;
        List<long> cumfilelength = null; // cumulative file length
        int filenumber; // currently open file
        FileStream fs = null;
        private bool Verbose = false;
        long pos; // current position
        public JoinStream(IList<string> fns, bool verb=false)
        {
            if (0 == fns.Count) throw new ApplicationException("No files match imput template");
            filenames = fns;
            cumfilelength=new List<long>(fns.Count);
            fs = null;
            long len = 0;
            pos = 0;
            Verbose = verb;
            foreach( string f in filenames ) {
                len+=(new FileInfo(f)).Length;
                cumfilelength.Add(len);
            }
            selectfile(0);
        }

        private bool selectfile(int fn)
        {
            if (fn >= filenames.Count) return false;
            filenumber = fn;
            fs = new FileStream(filenames[filenumber], FileMode.Open, FileAccess.Read);
            if (Verbose) Console.WriteLine("read {0}", filenames[filenumber]);
            return true;
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
#if false
            /*
             * straightforward implementation
             * timing: 98, 90
             */
            for (int i = 0; i < count; ++i)
            {
                int r = ReadByte();
                if (-1 == r ) return i;
                buffer[offset+i] = (byte)r;
            }
            return count;
#else
            int read;
            int totalread=0;
            pos += count;
            while (count > 0)
            {
                read = fs.Read(buffer, offset, count);
                totalread += read;
                if (read < count && !selectfile(1+filenumber)) return totalread;
                count -= read;
                offset += read;
            }
            return totalread;
            /* buffered version timed at 63 seconds */
#endif
        }

        public override int ReadByte()
        {
            int r;
            do
            {
                r = fs.ReadByte();
                if (r < 0 && !selectfile(1+filenumber)) return -1;
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
                            selectfile(i);
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
    public class aont
    {
        static int BufSize = 0x1000;
        // common parameters use for forward and reverse transform
        public static SymmetricAlgorithm Encryption = new AesManaged();
        public static int KEYSIZE = 256;
        static CipherMode MODE=CipherMode.CBC;
        static PaddingMode PADDING = PaddingMode.ISO10126;

        public static HashAlgorithm Hash = new SHA256Managed();

        static aont()
        {
            // Make random key and setup other parameters
            Encryption.KeySize = KEYSIZE;
            Encryption.Mode = MODE;
            Encryption.Padding = PADDING;
        }

        /// <summary>
        /// Estimates size of an individual part performing transformation and then splitting the result into several approximately equal parts.
        /// Include transformation overheads.
        /// </summary>
        /// <param name="inputlength">input length in bytes</param>
        /// <param name="partcount">number of parts</param>
        /// <returns></returns>
        public static long EstimateOutputPartLength(long inputlength, int partcount=1)
        {
            int b = Encryption.BlockSize / 8;
            inputlength += b - inputlength % b;
            inputlength += Hash.HashSize / 8 + b;
            return (inputlength / partcount) + (0 < inputlength % partcount ? 1 : 0);
        }

        public static void Transform(Stream file, Stream outstream)
        {
            Encryption.GenerateIV();
            Encryption.GenerateKey();
            using (Stream
                cs = new CryptoStream(file, Encryption.CreateEncryptor(), CryptoStreamMode.Read)
                )
            {
                byte[] buf = new byte[BufSize];
                int r;
                // Output IV
                outstream.Write(Encryption.IV, 0, Encryption.IV.Length);
                Hash.TransformBlock(Encryption.IV, 0, Encryption.IV.Length, buf, 0);
                while(0<(r=cs.Read(buf, 0, buf.Length)))
                {
                    // Encrypt each block with the random key
                    outstream.Write(buf, 0, r);
                    // Calculate hash of IV and all encrypted blocks
                    Hash.TransformBlock(buf, 0, r, buf, 0);
                }
                Hash.TransformFinalBlock(buf, 0, 0);

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

        // This method implements functionality, all other UnTransform method are wrappers around this one.
        public static void UnTransform(Stream infilestream, Stream outfilestream)
        {
            byte[] iv = new byte[Encryption.BlockSize / 8];
            
            byte[] storedkey = new byte[Encryption.KeySize / 8];
            long databytes = infilestream.Length - storedkey.Length;

            // first block is IV
            infilestream.Read(iv, 0, iv.Length);
            Encryption.IV = iv;

            byte[] buf = new byte[BufSize];
            int r;

            // ---- First pass ---- determine key

            // calculate Hash of first databytes bytes
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

            // read stored and mangled key
            r = infilestream.Read(storedkey, 0, storedkey.Length);
            if (r != storedkey.Length) throw new ApplicationException("cannot read final block");
            // unmangle key
            int j = 0;
            for (int i = 0; i < Hash.Hash.Length; ++i)
            {
                storedkey[i] = (byte)(storedkey[i] ^ Hash.Hash[j]);
                ++j;
                if (j >= Hash.Hash.Length) j -= Hash.Hash.Length;
            }
            // bytes of Encryption.Key are readonly, must change as a whole
            Encryption.Key = storedkey;

            // ---- Second pass ---- restore plaintext

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
