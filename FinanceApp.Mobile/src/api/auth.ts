import client from './client';
import { AuthResponse, LoginDto } from '../types';

export const login = (dto: LoginDto) =>
  client.post<AuthResponse>('/auth/login', dto);
