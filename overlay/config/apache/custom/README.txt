Put your own Apache .conf files here (extra virtual hosts, aliases,
per-site PHP versions). Kiln includes every *.conf file in this folder
and never overwrites them. Use ${KILN_ROOT} instead of absolute paths.

Example - config/apache/custom/shop.conf:

<VirtualHost *:${KILN_HTTP_PORT}>
    ServerName shop.localhost
    DocumentRoot "${KILN_ROOT}/www/shop/web"
    <Directory "${KILN_ROOT}/www/shop/web">
        AllowOverride All
        Require local
    </Directory>
</VirtualHost>
