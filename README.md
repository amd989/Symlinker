Symlinker
=========

![Screenshot](http://alejandro.md/publish/Symlinker/screenshot.jpg)

Back from the dead. Switched to GitHub instead of Google Code since they are going to close soon.

I will be updating this little app over the course of the next days and hopefully release it in better shape. A lot of people has filed issues with it and I plan to sort them out.

Better late than never some might say. I hope that is not too late for this app to get new light.

**Featured On**:

* [addictivetips](http://www.addictivetips.com/windows-tips/symlinker-create-symlink-hardlink-and-directory-junction-in-windows/)
* [TecFlap](http://www.tecflap.com/2012/05/29/software-day-winautohide-symlinker-hyperdesktop/)
* [Zhacks](http://www.zhacks.com/easily-create-symbolic-link-with-mklink-gui-symlinker/)

Previous Project link
https://code.google.com/p/symlinker/

Downloads
---------
[Download ClickOnce Installer (Supports auto updates)](http://bit.ly/symlinker_clickonce)

[Download Standalone Executable](http://bit.ly/symlinker_executable)

[Download ClickOnce Full Package](http://bit.ly/symlinker_package)


Overview
--------

With this utility you can use the symlink application Microsoft Windows has well hidden inside the cmd.exe app.

The goal is just make it easier to create symbolic links, hard links, or directory junctions, using a pretty simple interface, so no more bogus command line to do it...

This application needs .Net Framework 3.5 to run (SP1 recommended) and as of **4/24/2015** this software will only work under
* Windows Vista
* Windows 7
* Windows 8 and 8.1

Windows XP doesn't have the mklink command available from the cmd.exe app. 

If you encounter a bug, please let me know in the issues section, I will look into it when I have the time!

Thanks for your downloads and support, hope you like it!


TODO
----

* Get a real code signing certificate
* Rework the code to be better

Change Log
----------

Version 1.1.2.0 04/22/2015

* Added. Added ability to run as an administrator with UAC enabled, with this support for Windows 8 and 8.1 is done.
* Disabled. Unfortunately Drag&Drop does not work when running as an administrator [More Info](http://serverfault.com/questions/39600/why-cant-i-drag-drop-a-file-for-editing-in-notepad-in-windows-server-2008)

Version 1.1.1.15 04/22/2015

* Added. Added Drag&Drop into the directory fields
* Fixed. Finally Error Handling has been implemented, now the app will show you if the link wasn't created and why.

Version 1.1.1.14 04/22/2015

* Updated mail contact
* Moved issues from Google Code
* Updated installation link

Version 1.1.1.10 08/31/2013

* Uploaded to GitHub
* Added ClickOnce check for updates support, get updates as automatically upon app launch (Still work in progress)
* Minor Changes (Almost nothing)

Version 1.1.1.3 10/30/2010

* Fixed. Minor Bug Fixes. Thanks to some of you for pointing me out to them!.
* Modified. A little GUI redesign, just to support foreign Windows OS.
* Minor code revisions.
* TODO. Add UAC support for those folks with that enabled.
* TODO. Manage errors thrown by mklink, currently it just succeeds even if the link wasn't created confusing user.

Version 1.1.0.6 7/26/2010

* Fixed. Minor Bug Fixes.
* Added. Added back the functionality to choose network locations, it was disabled for an unknown reason :P.

Version 1.1.0.5 3/20/2010

* Fixed. Error changing the type of symbolic link from file to folder.
* Added. Tooltips with some information.

Version 1.1.0.3 2/6/2010

* Fixed. Display Microsoft as publisher of the application, now it displays the name of the app.

Version 1.1.0.0 2/3/2010

* Added. Functionality to create file symbolic links, previously only folder symlinks could be created, 
* -Thanks to jasoneg for the suggestion-.

Version 1.0.0.8 8/16/2009

* Fixed a minor bug when you try to create links in a folder that already has a folder equally named.
* Added better looking icon for Windows 7 users, though it will only work with the installable version.
