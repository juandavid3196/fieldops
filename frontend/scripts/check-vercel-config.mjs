// Fails the Vercel build while vercel.json still points the /api rewrite at the
// placeholder host, so a deployment can never ship with a non-working API proxy.
import { readFileSync } from 'node:fs';

const PLACEHOLDER = 'REPLACE-WITH-RAILWAY-API-DOMAIN';

const config = JSON.parse(readFileSync(new URL('../vercel.json', import.meta.url), 'utf8'));
const apiRewrite = (config.rewrites ?? []).find((rewrite) => rewrite.source === '/api/:path*');

if (!apiRewrite) {
  console.error('vercel.json has no "/api/:path*" rewrite.');
  process.exit(1);
}

if (!/^https:\/\/[^/]+\/:path\*$/.test(apiRewrite.destination)) {
  console.error('The /api rewrite destination must be "https://<railway-domain>/:path*".');
  process.exit(1);
}

if (apiRewrite.destination.includes(PLACEHOLDER)) {
  console.error(
    `vercel.json still contains the ${PLACEHOLDER} placeholder. ` +
      'Set the public HTTPS domain of the Railway API service (see docs/deployment/demo-deployment.md).',
  );
  process.exit(1);
}

console.log(`vercel.json: /api is proxied to ${new URL(apiRewrite.destination).origin}`);
