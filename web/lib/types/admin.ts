export interface AdminUser {
  id: string;
  email: string;
  displayName: string;
  isAdmin: boolean;
  mustChangePassword: boolean;
  createdAt: string;
}

export interface CreateUserRequest {
  email: string;
  displayName: string;
  isAdmin: boolean;
}

export interface CreateUserResponse {
  user: AdminUser;
  temporaryPassword: string;
}

export interface UpdateUserRequest {
  email?: string;
  displayName?: string;
  isAdmin?: boolean;
  resetPassword?: boolean;
}
