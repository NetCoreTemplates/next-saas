export const product = {
  name: process.env.productName ?? 'Acme',
  organizationName: process.env.organizationName ?? process.env.productName ?? 'Acme',
  description: process.env.productDescription ?? '',
  supportEmail: process.env.supportEmail ?? 'support@example.com',
  salesEmail: process.env.salesEmail ?? 'sales@example.com',
  privacyUrl: process.env.privacyUrl ?? '/privacy',
  termsUrl: process.env.termsUrl ?? '/terms',
}

export const productInitial = product.name.trim().charAt(0).toUpperCase() || 'S'
