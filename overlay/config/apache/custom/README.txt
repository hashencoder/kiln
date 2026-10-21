Put your own Apache .conf files here (extra virtual hosts, aliases,
per-site PHP versions). WebHut includes every *.conf file in this folder
and never overwrites them. Use ${WEBHUT_ROOT} instead of absolute paths.

Example - config/apache/custom/shop.conf:

<VirtualHost *:${WEBHUT_HTTP_PORT}>
    ServerName shop.localhost
    DocumentRoot "${WEBHUT_ROOT}/www/shop/web"
    <Directory "${WEBHUT_ROOT}/www/shop/web">
        AllowOverride All
        Require local
    </Directory>
</VirtualHost>
