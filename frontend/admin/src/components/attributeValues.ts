import type { AttributeDefinition, AttributeValues } from '../api'

export const fieldName = (code: string) => `attribute:${code}`

// Converts the submitted form back into the API's typed JSON values; empty fields are left out, which removes the value.
export function readAttributeValues(form: FormData, attributes: AttributeDefinition[]): AttributeValues {
  const values: AttributeValues = {}

  for (const attribute of attributes) {
    const name = fieldName(attribute.code)

    if (attribute.type === 'multiSelect') {
      const codes = form.getAll(name).map(String)
      if (codes.length > 0) {
        values[attribute.code] = codes
      }
      continue
    }

    const raw = String(form.get(name) ?? '')
    if (raw === '') {
      continue
    }

    values[attribute.code] =
      attribute.type === 'integer' || attribute.type === 'decimal' ? Number(raw) : attribute.type === 'boolean' ? raw === 'true' : raw
  }

  return values
}
