**Awagaman Mobile Deployment**

- App folder: `Awagaman.Mobile`
- Output folder after build: `Awagaman.Mobile/dist`
- Recommended host: same VPS as `Awagaman.Api`
- Recommended domain: `erp.yourdomain.com`

**Production env**

- Create `Awagaman.Mobile/.env.production`
- Put:

```env
VITE_API_BASE_URL=https://erp.yourdomain.com
```

**Local dev**

- Run API locally on `http://127.0.0.1:5088`
- Then run:

```powershell
cd "C:\amit sagu\awagaman project\ATL ERP_pre_multiuser\Awagaman.Mobile"
npm install
npm run dev
```

- Vite dev server proxies `/api` to local API automatically.

**Build**

```powershell
cd "C:\amit sagu\awagaman project\ATL ERP_pre_multiuser\Awagaman.Mobile"
npm run build
```

**Upload to VPS**

- Build first, then upload `dist` contents.

```powershell
cd "C:\amit sagu\awagaman project\ATL ERP_pre_multiuser\Awagaman.Mobile"
npm run build
scp -r ".\dist\*" root@187.127.157.47:/var/www/awagaman-mobile/
```

**Nginx setup on VPS**

- Copy `Awagaman.Mobile/nginx.awagaman-mobile.conf` to VPS, then place it in nginx sites.

```bash
ssh root@187.127.157.47
mkdir -p /var/www/awagaman-mobile
nano /etc/nginx/sites-available/awagaman-mobile
```

- Paste the content from `Awagaman.Mobile/nginx.awagaman-mobile.conf`

```bash
ln -sf /etc/nginx/sites-available/awagaman-mobile /etc/nginx/sites-enabled/awagaman-mobile
nginx -t
systemctl reload nginx
```

**HTTPS**

- After DNS points to the VPS, install TLS with Certbot.

```bash
apt update
snap install core; snap refresh core
snap install --classic certbot
ln -sf /snap/bin/certbot /usr/bin/certbot
certbot --nginx -d erp.yourdomain.com
```

**What this mobile app does**

- Login with existing ERP users
- Search and view:
  - Challan ledger
  - LR ledger
  - Bill ledger
- Read-only only

**What is not included yet**

- Add/edit/delete
- Offline ledger data
- Role-based mobile screen differences
- Dashboard analytics
