# ReleaseBuilder

This is a tool that uses XML files to publish releases. Probably could be done using make, 
but this uses XML which permits a much more tightly defined data, validation etc.

## Releasing release builder

publish the project using the profile PublishToFolder.pubxml

then copy the files to wherever on your local machine.

cd C:\Users\richa\source\repos\my-tools\rtools\ReleaseBuilder\bin\Release\net7.0\publish
copy /y * i:\apps\utils