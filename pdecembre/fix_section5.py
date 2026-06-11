#!/usr/bin/env python3
with open('csharp-implementation-proposal.md', 'r', encoding='utf-8') as f:
    lines = f.readlines()

output = []
i = 0
skip_historical = False

while i < len(lines):
    line = lines[i]
    
    # Skip any existing malformed Historical Origin section
    if 'Historical Origin' in line:
        skip_historical = True
        i += 1
        continue
    
    # Stop skipping when we hit section 5.1 or another section marker
    if skip_historical and (line.startswith('## 5.1') or line.startswith('Store only') or line.startswith('Optimization:')):
        skip_historical = False
    
    if skip_historical:
        i += 1
        continue
    
    # Insert historical section after "## 5. Sieve of Eratosthenes From Low Level Up"
    if line.strip() == '## 5. Sieve of Eratosthenes From Low Level Up':
        output.append(line)
        output.append('\n')
        output.append('### Historical Origin\n')
        output.append('The Sieve of Eratosthenes is one of the oldest known algorithms for finding prime numbers, dating back to ancient Greece around 240 BCE.\n')
        output.append('\n')
        output.append('Eratosthenes of Cyrene (c. 276–194 BCE) was a Greek mathematician, geographer, poet, and astronomer who served as the chief librarian at the Library of Alexandria. He is best known for:\n')
        output.append('\n')
        output.append('- Calculating the Earth\'s circumference with remarkable accuracy\n')
        output.append('- Creating one of the first known maps of the world\n')
        output.append('- Inventing this elegant prime-finding algorithm\n')
        output.append('\n')
        output.append('The algorithm is called a "sieve" because it systematically filters out composite numbers, leaving only primes behind—much like a physical sieve separates fine material from coarse. Ancient scholars would use a board with holes representing numbers, and physically remove pegs or counters at composite positions.\n')
        output.append('\n')
        output.append('The method\'s enduring value lies in its simplicity and efficiency. Over 2,200 years later, the core idea remains one of the fastest ways to generate many primes, and modern optimizations (like odd-only storage described below) build directly on Eratosthenes\' insight that multiples of primes cannot themselves be prime.\n')
        i += 1
        continue
    
    output.append(line)
    i += 1

with open('csharp-implementation-proposal.md', 'w', encoding='utf-8') as f:
    f.writelines(output)

print("Historical section added successfully")
