Symlinker
---------

Back from the dead. Switched to GitHub instead of Google Code since they are going to close soon.

I will be updating this little app over the course of the next days and hopefully release it in better shape. A lot of people has filed issues with it and I plan to sort them out.

Better later than never some might say. I hope that is not too late for this app to get new light.

Previous Project link
https://code.google.com/p/symlinker/

Download installer
http://alejandro.md/publish/Symlinker/Symlink%20Creator.application


Overview
--------

With this utility you can use the symlink application Microsoft Windows has well hidden inside the cmd.exe app.

The goal is just make it easier to create symbolic links, hard links, or directory junctions, using a pretty simple interface, so no more bogus command line to do it...

This application needs .Net Framework 3.5 to run (SP1 recommended) and as of 7/26/2010 this software will only work under Windows Vista or 7, Windows XP doesn't have the mklink command inside the cmd.exe app. I'll look for a workaround for this, but it isn't promising that I will find it...

If you encounter a bug, please let me know in the issues section, I will look into it when I have the time!

Thanks for your downloads and support, hope you like it!


TODO
----

Get a real certificate
Rework the code

Change Log
----------

Version 1.1.1.14 04/22/2015
Updated mail contact
Moved issues from Google Code
Updated installation link

Version 1.1.1.7 08/31/2013
Uploaded to GitHub
Added ClickOnce check for updates support, get updates as automatically upon app launch
Minor Changes (Almost nothing)

Version 1.1.1.3 10/30/2010

Fixed. Minor Bug Fixes. Thanks to some of you for pointing me out to them!.
Modified. A little GUI redesign, just to support foreign Windows OS.
Minor code revisions.
TODO. Add UAC support for those folks with that enabled.
TODO. Manage errors thrown by mklink, currently it just succeeds even if the link wasn't created confusing user.
Version 1.1.0.6 7/26/2010

Fixed. Minor Bug Fixes.
Added. Added back the functionality to choose network locations, it was disabled for an unknown reason :P.
Version 1.1.0.5 3/20/2010

Fixed. Error changing the type of symbolic link from file to folder.
Added. Tooltips with some information.
Version 1.1.0.3 2/6/2010

Fixed. Display Microsoft as publisher of the application, now it displays the name of the app.
Version 1.1.0.0 2/3/2010

Added. Functionality to create file symbolic links, previously only folder symlinks could be created, Thanks to jasoneg for the suggestion.
Version 1.0.0.8 8/16/2009

Fixed a minor bug when you try to create links in a folder that already has a folder equally named.
Added better looking icon for Windows 7 users, though it will only work with the installable version.
