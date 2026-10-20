KILN - portable Apache + PHP + MariaDB + Node.js for Windows
=============================================================

1. Put this "Kiln" folder somewhere short with no spaces, e.g. C:\kiln
   (not inside Program Files).

2. Double-click Kiln.exe.
   The first time, a setup window downloads the official Windows builds of
   Apache, PHP, MariaDB, phpMyAdmin, Node.js, Composer and the CA
   certificate bundle into this folder, then configures them.

3. Click "Start all", then open http://localhost

Your projects:   put them in www\<name>  ->  http://<name>.localhost
                 (click "Refresh sites" after adding a folder)
Node apps:       Kiln -> Node apps... -> Add
Database:        127.0.0.1:3306, user root, empty password by default
phpMyAdmin:      http://localhost/phpmyadmin
Terminal:        Kiln -> Shell (php, node, npm, composer, mariadb on PATH)
Command line:    kilnctl status | start | stop | restart | sites | php 8.4

Windows may show a SmartScreen warning the first time because the program
is new and not code-signed: click "More info" -> "Run anyway".

Kiln needs the Microsoft Visual C++ Redistributable 2017-2026 (x64). The
setup window checks for it and can install it for you.

LOCAL DEVELOPMENT ONLY. Everything listens on 127.0.0.1 and is not hardened
for production or for exposure to a network.

Full documentation: see the project's GitHub page.
