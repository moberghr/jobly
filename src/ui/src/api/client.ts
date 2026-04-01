import axios from 'axios';
import { config } from '@/config';

const apiPath = config.apiPath;

const api = axios.create({
  baseURL: apiPath,
});

// Global 401 handler — triggers login screen
let onUnauthorized: (() => void) | null = null;

export function setOnUnauthorized(handler: () => void) {
  onUnauthorized = handler;
}

api.interceptors.response.use(
  response => response,
  error => {
    if (error.response?.status === 401 && onUnauthorized) {
      onUnauthorized();
    }
    return Promise.reject(error);
  },
);

export default api;
