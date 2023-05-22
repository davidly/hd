// This app is optimized more than is necessary because it was fun.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Diagnostics;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;

class HexDump
{
    static void Usage()
    {
        Console.WriteLine( "Usage: hd [-a:d|x] [-f:(b|w|d|q)(d|x)] [-i] [-o:offset] [-n:bytes] [-c] [-w:offset ] file" );
        Console.WriteLine( "  Hex Dump" );
        Console.WriteLine( "  arguments: file  The file to display" );
        Console.WriteLine( "             -a    Specify d for decimal or x for hex display of address/offset. Default Hex" );
        Console.WriteLine( "             -b    Show data in Big Endian. Default is Little Endian" );
        Console.WriteLine( "             -c    Don't display ascii characters on the right. Default is to display it." );
        Console.WriteLine( "             -d    Ignore all other formatting args and write unformatted hex bytes" );
        Console.WriteLine( "             -e    Indicates -o is from the End of the file, not the start" );
        Console.WriteLine( "             -f    Format of data: width (Byte, Word, Dword, Qword) and radix (Decimal, heX). Default bx" );
        Console.WriteLine( "             -i    Ignore other formatting and wrte a C++ initialized byte array" );
        Console.WriteLine( "             -n    Count of bytes to display (decimal or hex). 0 for whole file. Default is 0x100" );
        Console.WriteLine( "             -o    Starting Offset in bytes (decimal or hex). Default is 0" );
        Console.WriteLine( "             -q    Ignore other formatting and write a C++ initialized quadword (8-byte) array" );
        Console.WriteLine( "             -w    Ignore other formatting and write output in Apple 1 hex format at the address" );
        Console.WriteLine( "  defaults:  -a:x -f:bx -o:0 -n:0x100" );
        Console.WriteLine( "  examples:  hd in.txt" );
        Console.WriteLine( "             hd -a:d -f:qx -o:32 -n:64 -c in.dll       dumps the first 64 bytes" );
        Console.WriteLine( "             hd -f:dd -o:0x40 -n:0x200 -c foo.exe      dumps the first 0x200 bytes" );
        Console.WriteLine( "             hd -f:qx -o:0x40 -n -c foo.exe            dumps the whole file" );
        Console.WriteLine( "             hd -f:bd -o:0x40 -n:0 -c foo.exe          dumps the whole file" );
        Console.WriteLine( "             hd foo.bin -w:0x1000                      dumps in Apple 1 format base address 0x1000" );

        Environment.Exit(1);
    } //Usage

    public static Stopwatch stopWatch = new Stopwatch();
    public static long timeWriting = 0;

    class SixteenBytes
    {
        byte [] data;
        int offset;

        public SixteenBytes()
        {
            data = new byte[ 16 ];
            offset = 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public byte ReadByte()
        {
            return data[ offset++ ];
        }

        public ulong ReadQWord()
        {
            ulong x = 0;
            for ( int i = 0; i < 8; i++ )
                x |= ( ( (ulong) data[ offset++ ] ) << ( 8 * i ) );
            return x;
        }

        public void Rewind()
        {
            offset = 0;
        }

        public void Read( FileStream fs, int len )
        {
            fs.Read( data, 0, len );

            // Let the caller read beyond the end of valid data, but make sure it's 0s

            for ( int i = len; i < 16; i++ )
                data[ i ] = 0;

            offset = 0;
        }
    } //SixteenBytes

    // This custom hex rendering is 2x faster for Byte and 7% faster for QByte

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static void AppendHexNibble( StringBuilder sb, int n )
    {
        if ( n < 10 )
            sb.Append( (char) ( n + 48 ) );
        else
            sb.Append( (char) ( n + 87 ) );
    } //AppendHexNibble

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static void AppendHexByte( StringBuilder sb, byte b )
    {
        AppendHexNibble( sb, (int) ( 0xf & ( b >> 4 ) ) );
        AppendHexNibble( sb, (int) ( 0xf & b ) );
    } //AddHex

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static void AppendHexWord( StringBuilder sb, ushort us )
    {
        AppendHexByte( sb, (byte) ( us >> 8 ) );
        AppendHexByte( sb, (byte) ( 0xff & us ) );
    } //AppendHexDWord

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static void AppendHexDWord( StringBuilder sb, uint ui )
    {
        AppendHexWord( sb, (ushort) ( ui >> 16 ) );
        AppendHexWord( sb, (ushort) ( 0xffff & ui ) );
    } //AppendHexDWord

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static void AppendHexQWord( StringBuilder sb, ulong ul )
    {
        AppendHexDWord( sb, (uint) ( ul >> 32 ) );
        AppendHexDWord( sb, (uint) ( 0xffffffff & ul ) );
    } //AppendHexQWord

    class ThreadInfo
    {
        public string s;
        public AutoResetEvent evDone;

        public ThreadInfo( AutoResetEvent ev )
        {
            s = null;
            evDone = ev;
        }
    } //ThreadInfo

    public static void WorkerThreadProc( Object stateInfo )
    {
        ThreadInfo ti = (ThreadInfo) stateInfo;

        long start = stopWatch.ElapsedMilliseconds;
        Console.Write( ti.s );
        long end = stopWatch.ElapsedMilliseconds;

        timeWriting += ( end - start );

        ti.evDone.Set();
    } //WorkerThreadProc

    static void Main( string[] args )
    {
        stopWatch.Start();
        string inFile = null;
        bool hexAddress = true;
        bool displayASCII = true;
        int dataWidth = 1;
        bool hexRadix = true;
        long displayedBytes = 0x100; 
        long startingOffset = 0;
        bool bigEndian = false;
        bool hexDump = false;
        bool offsetFromEnd = false;
        bool cppArray = false;
        bool cppArrayQWord = false;
        bool apple1 = false;
        long apple1Offset = 0x1000;

        for ( int i = 0; i < args.Length; i++ )
        {
            if ( '-' == args[i][0] || '/' == args[i][0] )
            {
                string argUpper = args[i].ToUpper();
                string arg = args[i];
                char c = argUpper[1];
    
                if ( 'A' != c && 'B' != c && 'C' != c && 'D' != c && 'E' != c && 'F' != c && 'I' != c && 'N' != c && 'O' != c && 'Q' != c && 'W' != c )
                    Usage();
    
                if ( 'A' == c )
                {
                    if ( ( arg.Length != 4 ) || ( ':' != arg[2] ) )
                        Usage();

                    char address = argUpper[3];

                    if ( 'D' == address )
                        hexAddress = false;
                    else if ( 'X' == address )
                        hexAddress = true;
                    else
                        Usage();
                }
                else if ( 'B' == c )
                    bigEndian = true;
                else if ( 'C' == c )
                    displayASCII = false;
                else if ( 'D' == c )
                    hexDump = true;
                else if ( 'E' == c )
                    offsetFromEnd = true;
                else if ( 'F' == c )
                {
                    if ( ( arg.Length != 5 ) || ( ':' != arg[2] ) )
                        Usage();

                    Char width = argUpper[3];
                    Char radix = argUpper[4];

                    if ( 'B' == width )
                        dataWidth = 1;
                    else if ( 'W' == width )
                        dataWidth = 2;
                    else if ( 'D' == width )
                        dataWidth = 4;
                    else if ( 'Q' == width )
                        dataWidth = 8;
                    else
                        Usage();

                    if ( 'D' == radix )
                        hexRadix = false;
                    else if ( 'X' == radix )
                        hexRadix = true;
                    else
                        Usage();
                }
                else if ( 'I' == c )
                    cppArray = true;
                else if ( 'N' == c )
                {
                    if ( arg.Length == 2 )
                    {
                        displayedBytes = Int64.MaxValue;
                    }
                    else
                    {
                        if ( ( arg.Length <= 3 ) || ( ':' != arg[2] ) )
                            Usage();

                        if ( ( arg.Length >= 5 ) && ( '0' == argUpper[3] ) && ( 'X' == argUpper[4] ) )
                            displayedBytes = Convert.ToInt64( arg.Substring( 3 ), 16 );
                        else
                            displayedBytes = Convert.ToInt64( arg.Substring( 3 ) );

                        if ( 0 == displayedBytes )
                            displayedBytes = Int64.MaxValue;
                    }
                }
                else if ( 'O' == c )
                {
                    if ( ( arg.Length <= 3 ) || ( ':' != arg[2] ) )
                        Usage();

                    if ( ( arg.Length >= 5 ) && ( '0' == argUpper[3] ) && ( 'X' == argUpper[4] ) )
                        startingOffset = Convert.ToInt64( arg.Substring( 3 ), 16 );
                    else
                        startingOffset = Convert.ToInt64( arg.Substring( 3 ) );
                }
                else if ( 'Q' == c )
                    cppArrayQWord = true;
                else if ( 'W' == c )
                {
                    if ( ( arg.Length <= 3 ) || ( ':' != arg[2] ) )
                        Usage();

                    displayedBytes = Int64.MaxValue;
                    apple1 = true;
                    if ( ( arg.Length >= 5 ) && ( '0' == argUpper[3] ) && ( 'X' == argUpper[4] ) )
                        apple1Offset = Convert.ToInt64( arg.Substring( 3 ), 16 );
                    else
                        apple1Offset = Convert.ToInt64( arg.Substring( 3 ) );
                }
            }
            else
            {
                if ( inFile == null )
                    inFile = args[i];
                else
                    Usage();
            }
        }
    
        if ( null == inFile )
            Usage();

        AutoResetEvent evDone = new AutoResetEvent( false );
        ThreadInfo threadInfo = new ThreadInfo( evDone );
        bool outstandingWrite = false;

        // Performance notes: Using StringBuilder is 37x faster since Console.WriteX is very slow
        //                    Using SixteenBytes is 3x faster since FileStream I/O is slow
        //                    Using more than 16 bytes per read doesn't have a significant impact
        //                    75% of time is spend in Console.WriteLine() for large files, so make it threaded.

        try
        {
            using ( FileStream fs = new FileStream( inFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 64 * 1024, FileOptions.SequentialScan ) ) 
            {
                if ( offsetFromEnd )
                {
                    if ( 0 != startingOffset && fs.Length >= startingOffset )
                        startingOffset = fs.Length - startingOffset;
                    else if ( fs.Length >= displayedBytes )
                        startingOffset = fs.Length - displayedBytes;
                }
                else if ( startingOffset >= fs.Length )
                {
                    Console.WriteLine( "starting offset {0} can't be beyond length of file {1}", startingOffset, fs.Length );
                    Usage();
                }

                displayedBytes = Math.Min( ( fs.Length - startingOffset ), displayedBytes );

                fs.Seek( startingOffset, SeekOrigin.Begin );

                StringBuilder sb = new StringBuilder( 1024 * 1024 * 2 );
                SixteenBytes buf = new SixteenBytes();

                for ( int offset = 0; offset < displayedBytes; offset += 16 )
                {
                    int len = ( ( offset + 16 ) > displayedBytes ) ? (int) ( displayedBytes - offset ) : 16;
                    buf.Read( fs, len );
                    long cap = Math.Min( offset + 16, displayedBytes );

                    if ( cppArray )
                    {
                        for ( long o = offset; o < cap; o++ )
                        {
                            if ( 0 == ( o % 16 ) )
                                sb.Append( "\n" );
                            sb.Append( "0x" );
                            AppendHexByte( sb, buf.ReadByte() );
                            sb.Append( ", " );
                        }
                        continue;
                    }
                    else if ( apple1 )
                    {
                        for ( long o = offset; o < cap; o++ )
                        {
                            if ( ( 0 != o ) && 0 == ( o % 8 ) )
                                sb.Append( "\r\n" );

                            if ( 0 == ( o % 8 ) )
                            {
                                long addr = o + apple1Offset;
                                AppendHexWord( sb, (ushort) addr );
                                sb.Append( ":" );
                            }

                            sb.Append( " " );
                            AppendHexByte( sb, buf.ReadByte() );
                        }
                        continue;
                    }
                    else if ( cppArrayQWord )
                    {
                        if ( ( 0 != offset ) && 0 == ( offset % 64 ) )
                            sb.Append( "\n" );

                        ulong q = buf.ReadQWord();
                        sb.Append( "0x" );
                        AppendHexQWord( sb, q );
                        sb.Append( ", " );

                        if ( cap > offset + 8 )
                        {
                            q = buf.ReadQWord();
                            sb.Append( "0x" );
                            AppendHexQWord( sb, q );
                            sb.Append( ", " );
                        }
                        continue;
                    }

                    if ( hexDump )
                    {
                        for ( long o = offset; o < cap; o++ )
                            AppendHexByte( sb, buf.ReadByte() );
                        continue;
                    }    

                    if ( hexAddress )
                    {
                        AppendHexQWord( sb, (ulong) ( offset + startingOffset ) );
                        sb.Append( ' ', 2 );
                    }
                    else
                        sb.AppendFormat( "{0,16:D16}  ", offset + startingOffset);
    
                    long spaceNeeded = 0;
    
                    if ( 1 == dataWidth )
                    {
                        for ( long o = offset; o < cap; o++ )
                        {
                            if ( hexRadix )
                            {
                                AppendHexByte( sb, buf.ReadByte() );
                                sb.Append( ' ' );
                            }
                            else
                                sb.AppendFormat( "{0,3:D3} ", buf.ReadByte() );
                        }
    
                        spaceNeeded = ( 16 - ( cap - offset ) ) * ( hexRadix ? 3 : 4 );
                    }
                    else if ( 2 == dataWidth )
                    {
                        for ( long o = offset; o < cap; o += 2 )
                        {
                            ushort us = ( bigEndian ) ?                              
                                        (ushort) ( ( buf.ReadByte() << 8 )   |
                                                   ( buf.ReadByte()      ) )
                                      :                                    
                                        (ushort) ( ( buf.ReadByte() )        |
                                                   ( buf.ReadByte() << 8 ) );

                            if ( hexRadix )
                            {
                                AppendHexWord( sb, us );
                                sb.Append( ' ' );
                            }
                            else
                                sb.AppendFormat( "{0,5:D5} ", us );
                        }
    
                        // the formatting above rounds up and the math below rounds down
    
                        spaceNeeded = ( ( 16 - ( cap - offset ) ) / 2 ) * ( hexRadix ? 5 : 6 );
                    }
                    else if ( 4 == dataWidth )
                    {
                        for ( long o = offset; o < cap; o += 4 )
                        {
                            uint ui = ( bigEndian ) ?
                                      ( (uint) buf.ReadByte() << 24 )  |
                                      ( (uint) buf.ReadByte() << 16 )  |
                                      ( (uint) buf.ReadByte() << 8  )  |
                                      ( (uint) buf.ReadByte()       )
                                    :
                                      ( (uint) buf.ReadByte() )        |
                                      ( (uint) buf.ReadByte() << 8  )  |
                                      ( (uint) buf.ReadByte() << 16 )  |
                                      ( (uint) buf.ReadByte() << 24 );
    
                            if ( hexRadix )
                            {
                                AppendHexDWord( sb, ui );
                                sb.Append( ' ' );

                                //sb.AppendFormat( "{0,8:x8} ", ui );
                            }
                            else
                                sb.AppendFormat( "{0,10:D10} ", ui );
                        }
    
                        spaceNeeded = ( ( 16 - ( cap - offset ) ) / 4 ) * ( hexRadix ? 9 : 11 );
                    }
                    else if ( 8 == dataWidth )
                    {
                        for ( long o = offset; o < cap; o += 8 )
                        {
                            ulong ul = ( bigEndian ) ?
                                       ( (ulong) buf.ReadByte() << 56 ) |
                                       ( (ulong) buf.ReadByte() << 48 ) |
                                       ( (ulong) buf.ReadByte() << 40 ) |
                                       ( (ulong) buf.ReadByte() << 32 ) |
                                       ( (ulong) buf.ReadByte() << 24 ) |
                                       ( (ulong) buf.ReadByte() << 16 ) |
                                       ( (ulong) buf.ReadByte() << 8  ) |
                                       ( (ulong) buf.ReadByte()       )
                                     :
                                       ( (ulong) buf.ReadByte() )       |
                                       ( (ulong) buf.ReadByte() << 8  ) |
                                       ( (ulong) buf.ReadByte() << 16 ) |
                                       ( (ulong) buf.ReadByte() << 24 ) |
                                       ( (ulong) buf.ReadByte() << 32 ) |
                                       ( (ulong) buf.ReadByte() << 40 ) |
                                       ( (ulong) buf.ReadByte() << 48 ) |
                                       ( (ulong) buf.ReadByte() << 56 );

                            if ( hexRadix )
                            {
                                AppendHexQWord( sb, ul );
                                sb.Append( ' ' );
                                //sb.AppendFormat( "{0,16:x16} ", ul );
                            }
                            else
                                sb.AppendFormat( "{0,20:D20} ", ul );
                        }
    
                        spaceNeeded = ( ( 16 - ( cap - offset ) ) / 8 ) * ( hexRadix ? 17 : 21 );
                    }
    
                    if ( displayASCII )
                    {
                        sb.Append( ' ', (int) spaceNeeded );
    
                        buf.Rewind();

                        for ( long o = offset; o < cap; o++ )
                        {
                            char ch = (char) buf.ReadByte();

                            // don't try to print control characters including ^Z, BEL, BS, HT, LF, VT, FF, CR, EOF, etc.

                            if ( ( 0xff == ch ) || ( ( ch >= 0 ) && ( ch <= 0x1f ) ) )
                                ch = '.';
                            sb.Append( ch );
                        }
                    }
    
                    sb.Append( '\n' );
    
                    if ( sb.Length > ( sb.Capacity / 2 ) )
                    {
                        if ( outstandingWrite )
                        {
                            evDone.WaitOne();
                            outstandingWrite = false;
                        }

                        threadInfo.s = sb.ToString();
                        outstandingWrite = true;

                        ThreadPool.QueueUserWorkItem( new WaitCallback( WorkerThreadProc ), threadInfo );

                        sb.Clear();
                    }
                } //for

                if ( outstandingWrite )
                {
                    evDone.WaitOne();
                    outstandingWrite = false;
                }

                if ( sb.Length > 0 )
                {
                    Console.Write( sb.ToString() );
                    sb.Clear();
                }

                //Console.WriteLine( "time writing: {0}", timeWriting );
            } //using
        }
        catch (Exception e)
        {
            Console.WriteLine( "hd.exe caught an exception {0}", e.ToString() );
            Usage();
        }
    } //Main
} //HexDump

