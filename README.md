aont
====

All-or-nothing transform
------------------------

Applies a transformation to a file, which makes this file unintelligible.
This transformation is easy to reverse and bring the file back to its
original readable form, unless portion of the transformed file is missing
or corrupt.
In this case it is difficult (as difficult as recovery of AES-256 encrypted
data with unknown key) to reconstruct any remaining portion of the original
file.
The corrupt file will not necessarily fail restoration attempt. It may be
restored to a garbage. The integrity of a restored file should be ensured with
different means, for example with a hash value stored separately.

Can be used in conjunction with 'split' utility to split file into pieces
which can be meaningfully reassembled only when all come together.

Usage:

    aont [/t]  [/n:number] [/s:size] [inputfile] [outputfile|name-template]
    aont /r inputfiles outputfile
    aont /v inputfiles

`/t` -- apply all-or-nothing transformation to a file

`/n:`*number* -- split the output into *number* parts

`/s:`*size*   -- split output into parts, each of size *size*

`/r` -- restore file to its original form and write result to outputfile

`/v` -- display which files and in what order will be processed

If outputfile name is missing, it is genereated by appending suffix `.aont` for
forward transformation and `.restored` for reverse transformation.

Use modes
---------

### Long term storage

The transformed part is split into large part, containing bulk of the data and
one or more smaller (say, 1K) parts. Bulk part is stored on publicly accessible
server, where it enjoys regular backups and other services provided by the
server. The smaller parts are stored in secure locations. They can be stored on
individual floppies or USB drives, so that their 
Even though bulk part is publicly accessible, it is useless without smaller
parts. The smaller parts are easier to store in a secure way, in part because
of their physical manifestations provided by portable media.

### Secret sharing

3/3: Split file into 3 approximately equal parts and give each part to a
trusted individual. Only when all 3 agree to give their share, the original
file can be restored.

Other examples of sharing schemes:

2/3: Split file into 3 approximatley equal parts.

* Give parts 2,3 to trustee A
* Give parts 1,3 to trustee B
* Give parts 1,2 to trustee C

This way any 2 of them together can restore the file, but none individually.

2/4: distribute 4 parts among 4 individuals:

* (2,3,4) --> A
* (1,3,4) --> B
* (1,2,4) --> C
* (1,2,3) --> D

3/4: distribute 6 parts among 4 individuals:

* (4,5,6) -> A
* (2,3,6) -> B
* (1,3,5) -> C
* (1,2,4) -> D

3/5: distribute 10 parts among 5 individuals:

* (5,6,7,8,9,10)->A
* (2,3,4,8,9,10)->B
* (1,3,4,6,7,10)->C
* (1,2,4,5,7,9) ->D
* (1,2,3,5,6,8) ->E

All these schemes may be implemented with one more part -- containing bulk of a
file and parts that represent shares are small (64-1024 bytes). This bulk part
may be located on public server or copy of it may be stored with each trustees.
Either way, it does not have to be secret.

Command line examples
---------------------

    aont /n:4 /s:1k 2011-11-20.zip

Transform file `2011-11-20.zip`, splits in 4 pieces and place pieces of
transofrmed result into `2011-11-20.zip.part1`, `2011-11-20.zip.part2`,
`2011-11-20.zip.part3`, `2011-11-20.zip.part4`. Parts 1-3 are 1 kb each. Part 4
contains the rest of the transformation.

    aont /n:4 /s:1k 2011-11-20.zip 20111120.aont*

Specify template for part names 20111120.aont1, 20111120.aont2, 20111120.aont3, 20111120.aont4.


    aont /r 20111120.part*

Apply reverse transformation to a concatenation of `20111120.part1`,
`20111120.part2`, ... and write the result to `20111120.restored`


    for %F in (*.pdf) do aont /t /s:256 /n:4 "%F" "t\%F.{0:D3}"

Process all `.pdf` files in current directory and place resulting parts in t
subdirectory. The name of parts are original file names with suffix of a form
`.001`.

    for %F in (t\*.part1) do aont /r "t\%~nF.part*" "r\%~nF"

Assume that `.\t` subdirectory contains all necessary parts named
`{orignalfilename}.part{N}`. Apply reverse transformation to sets of files with
same originalname and place result in `.\r` subdirectory.


Description of transformation
-----------------------------

    0    +-----------------------
         | IV (randomly generated)
    16   +-----------------------
         | data encrypted with AES
         | with a random 256 bit key K
         | in CBC mode
    N-32 +-----------------------
         | xor SHA256(preceeding blocks)
    N    +-----------------------
   






