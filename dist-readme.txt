SERVNOOK - portable Apache + PHP + MariaDB + Node.js for Windows
=============================================================

1. Put this "ServNook" folder somewhere short with no spaces, e.g. C:\servnook
   (not inside Program Files).

2. Double-click ServNook.exe (the control panel and installer).
   snook.exe is the command-line tool for terminals and scripts; if you
   double-click it, it simply opens ServNook.exe for you.
   The first time, a setup window downloads the official Windows builds of
   Apache, PHP, MariaDB, phpMyAdmin, Node.js, Composer and the CA
   certificate bundle into this folder, then configures them.

3. Click "Start all", then open http://localhost

Your projects:   put them in www\<name>  ->  http://<name>.localhost
                 (click "Refresh sites" after adding a folder)
Node apps:       ServNook -> Node apps -> Add
Database:        127.0.0.1:3306, user root, empty password by default
Backups:         automatic every 2 hours and before stopping (data\backups);
                 ServNook -> Database tools to restore or rebuild after a crash
From XAMPP:      stop MySQL in XAMPP, then ServNook -> Database tools -> Import from XAMPP
phpMyAdmin:      http://localhost/phpmyadmin
Terminal:        ServNook -> Shell (php, node, npm, composer, mariadb on PATH)
Command line:    snook status | start | stop | restart | sites | php 8.4

Windows may show a SmartScreen warning the first time because the program
is new and not code-signed: click "More info" -> "Run anyway".

ServNook needs the Microsoft Visual C++ Redistributable 2017-2026 (x64). The
setup window checks for it and can install it for you.

LOCAL DEVELOPMENT ONLY. Everything listens on 127.0.0.1 and is not hardened
for production or for exposure to a network.

Full documentation: see the project's GitHub page.
