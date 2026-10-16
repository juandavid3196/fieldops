import { Component, computed, inject, input, signal } from '@angular/core';

import { API_CONFIG, buildApiUrl } from '../../../core/config/api.config';

/**
 * Decorative avatar (account-settings BR-08): the photo through an `<img>` whose URL changes with
 * `photoUpdatedAt`, or the initials when there is no photo or it fails to load. Size comes from
 * the `--fo-avatar-size` custom property of the host.
 */
@Component({
  selector: 'app-user-avatar',
  templateUrl: './user-avatar.html',
  styleUrl: './user-avatar.scss',
})
export class UserAvatar {
  private readonly config = inject(API_CONFIG);

  readonly initials = input.required<string>();
  readonly photoUpdatedAt = input<string | null>(null);

  private readonly failedUrl = signal<string | null>(null);

  readonly photoUrl = computed(() => {
    const version = this.photoUpdatedAt();
    return version === null
      ? null
      : `${buildApiUrl(this.config, 'account/photo')}?v=${encodeURIComponent(version)}`;
  });
  readonly showPhoto = computed(
    () => this.photoUrl() !== null && this.photoUrl() !== this.failedUrl(),
  );

  onError(): void {
    this.failedUrl.set(this.photoUrl());
  }
}
