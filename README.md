# hd
Hex Dump. Shows portions of files in various forms. Windows command line app.

To build, use yoru favorite version of .net:

    c:\windows\microsoft.net\framework64\v4.0.30319\csc.exe /debug+ /checked- /nologo /o+ /nowarn:0168 hd.cs

Usage:

    Usage: hd [-a:d|x] [-f:(b|w|d|q)(d|x)] [-o:offset] [-n:bytes] [-c] file
    
      Hex Dump
      arguments: file  The file to display
                 -a    Specify d for decimal or x for hex display of address/offset
                 -b    Show data in Big Endian. Default is Little Endian
                 -c    Don't display ascii characters on the right
                 -d    Ignore all other formatting args and write unformatted hex bytes
                 -e    Indicates -o is from the End of the file, not the start
                 -f    Format of data: width (Byte, Word, Dword, Qword) and radix (Decimal, Hex)
                 -n    Count of bytes to display (decimal or hex). 0 for whole file. Default is 0x100
                 -o    Starting Offset in bytes (decimal or hex)
      defaults:  -a:x -f:bx -o:0 -n:0x100
      examples:  hd in.txt
                 hd -a:d -f:qx -o:32 -n:64 -c in.dll       dumps the first 64 bytes
                 hd -f:dd -o:0x40 -n:0x200 -c foo.exe      dumps the first 0x200 bytes
                 hd -f:qx -o:0x40 -n -c foo.exe            dumps the whole file
                 hd -f:bd -o:0x40 -n:0 -c foo.exe          dumps the whole file

The app is perhaps overly optimized, because sometimes you need to dump a massive file to look for patterns.
