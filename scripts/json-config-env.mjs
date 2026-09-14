import { readFileSync } from 'node:fs'

const file = process.argv[2]
if (!file) throw new Error('A JSON configuration file is required.')

const values = JSON.parse(readFileSync(file, 'utf8'))
const write = (value, path = []) => {
  if (value !== null && typeof value === 'object') {
    for (const [key, child] of Object.entries(value)) write(child, [...path, key])
    return
  }
  process.stdout.write(`${path.join('__')}=${value ?? ''}\0`)
}
write(values)
