import struct,sys,os
def read_wak(p):
    f=open(p,'rb').read()
    _,n,toc,_=struct.unpack_from('<4i',f,0)
    pos=16; out={}
    for i in range(n):
        off,size,nl=struct.unpack_from('<3i',f,pos); pos+=12
        name=f[pos:pos+nl].decode('utf-8'); pos+=nl
        out[name]=(off,size)
    return f,out
if __name__=='__main__':
    f,t=read_wak(sys.argv[1])
    print(len(t))
    want=sys.argv[2:]
    for name in t:
        if any(w in name for w in want):
            o,s=t[name]
            dst=os.path.join(os.path.expanduser('~/noita-lab/x'),name)
            os.makedirs(os.path.dirname(dst),exist_ok=True)
            open(dst,'wb').write(f[o:o+s])
