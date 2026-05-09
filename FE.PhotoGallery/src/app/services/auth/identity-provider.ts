export enum IdentityProviderType {
    Google = 'Google',
    Microsoft = 'Microsoft',
    Facebook = 'Facebook',
    Apple = 'Apple',
    Custom = 'Custom'
}

export interface IdentityProvider {
    /**
     * Triggers the IdP login process and resolves with the raw ID token (e.g. from Google).
     */
    signIn(): Promise<string>;

    /**
     * Triggers the IdP logout process.
     */
    signOut(): Promise<void>;

    /**
     * Refreshes the ID token if supported by the provider.
     */
    refresh(): Promise<string>;

    /**
     * Validates the current ID token (if possible client-side).
     */
    validate(token: string): boolean;

    /**
     * The name of the provider (e.g. "Google", "Microsoft").
     */
    readonly name: IdentityProviderType;

    /**
     * The container ID where the button will be rendered.
     */
    readonly containerId: string;

    /**
     * The token received from the provider after successful login.
     */
    token?: string;

    /**
     * Render the Button for the provider
     */
    renderButton(containerId: string): void;
}
