Put your own Apache .conf files here (extra virtual hosts, aliases,
per-site PHP versions). ServNook includes every *.conf file in this folder
and never overwrites them. Use ${SERVNOOK_ROOT} instead of absolute paths.

Example - config/apache/custom/shop.conf:

<VirtualHost *:${SERVNOOK_HTTP_PORT}>
    ServerName shop.localhost
    DocumentRoot "${SERVNOOK_ROOT}/www/shop/web"
    <Directory "${SERVNOOK_ROOT}/www/shop/web">
        AllowOverride All
        Require local
    </Directory>
</VirtualHost>
