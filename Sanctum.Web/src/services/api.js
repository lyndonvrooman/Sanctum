export async function apiFetch(path, options = {}) {
  const headers = {
    'Content-Type': 'application/json',
    ...(options.headers || {})
  };

  const devEmail = localStorage.getItem('sanctum_dev_email');
  if (devEmail) {
    headers['X-Dev-User-Email'] = devEmail;
  }

  const res = await fetch(path, { ...options, headers });
  if (!res.ok) throw new Error(await res.text());
  return res.status === 204 ? null : res.json();
}
