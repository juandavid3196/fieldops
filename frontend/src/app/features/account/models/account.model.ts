/** Metadata of the stored profile photo (`photo` of the profile and the `PUT /account/photo` body). */
export interface AccountPhoto {
  readonly contentType: string;
  readonly sizeBytes: number;
  readonly updatedAt: string;
}

/** Body of `GET`/`PUT /account/profile`. */
export interface AccountProfile {
  readonly firstName: string;
  readonly lastName: string;
  readonly email: string;
  readonly phone: string | null;
  readonly organization: { readonly name: string };
  readonly role: { readonly name: string };
  readonly photo: AccountPhoto | null;
}

/** Body of `PUT /account/profile`; never carries an identifier. */
export interface UpdateProfileRequest {
  readonly firstName: string;
  readonly lastName: string;
  readonly phone: string | null;
}

/** Body of `POST /account/password`. */
export interface ChangePasswordRequest {
  readonly currentPassword: string;
  readonly newPassword: string;
}

/** Upload progress (`percent` is `null` when the size is unknown) or the stored metadata. */
export type PhotoUploadEvent =
  | { readonly kind: 'progress'; readonly percent: number | null }
  | { readonly kind: 'done'; readonly photo: AccountPhoto };
