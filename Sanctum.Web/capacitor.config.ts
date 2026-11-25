import type { CapacitorConfig } from '@capacitor/cli';

const config: CapacitorConfig = {
  appId: 'ca.kinkyfairytales.sanctum',
  appName: 'Sanctum',
  webDir: 'dist',
  server: {
    androidScheme: 'https'
  }
};

export default config;
