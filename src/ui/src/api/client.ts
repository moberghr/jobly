import axios from 'axios';

const apiPath = (window as unknown as Record<string, string>).apiPath || '/jobly/api';

const api = axios.create({
  baseURL: apiPath,
});

export default api;
