import axios from 'axios';

const api = axios.create({
  baseURL: '/dashboard/api',
});

export default api;
