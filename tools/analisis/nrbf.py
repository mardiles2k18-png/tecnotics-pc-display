"""Minimal .NET BinaryFormatter (MS-NRBF) reader."""
import struct, io
class Ref:
    def __init__(s,i): s.id=i
    def __repr__(s): return f"<ref {s.id}>"
class NullRun:
    def __init__(s,n): s.n=n
class Obj(dict):
    def __init__(s,cls,lib=None): super().__init__(); s.cls=cls; s.lib=lib
PRIM={1:'?',2:'B',3:'c',6:'d',7:'h',8:'i',9:'q',10:'b',11:'f',13:'q',14:'H',15:'I',16:'Q',12:'decimal',18:'str',16:'Q'}
class Reader:
    def __init__(s,data):
        s.f=io.BytesIO(data); s.objs={}; s.classes={}; s.libs={}
    def u8(s): return s.f.read(1)[0]
    def i32(s): return struct.unpack('<i',s.f.read(4))[0]
    def lpstr(s):
        n=0;sh=0
        while True:
            b=s.u8(); n|=(b&0x7f)<<sh; sh+=7
            if not b&0x80: break
        return s.f.read(n).decode('utf-8')
    def prim(s,t):
        if t==1: return s.u8()!=0
        if t==2: return s.u8()
        if t==3:
            b=s.f.read(1); 
            while True:
                try: return b.decode('utf-8')
                except: b+=s.f.read(1)
        if t==5: return s.lpstr()
        if t==6: return struct.unpack('<d',s.f.read(8))[0]
        if t==7: return struct.unpack('<h',s.f.read(2))[0]
        if t==8: return s.i32()
        if t==9: return struct.unpack('<q',s.f.read(8))[0]
        if t==10: return struct.unpack('<b',s.f.read(1))[0]
        if t==11: return struct.unpack('<f',s.f.read(4))[0]
        if t==12: return struct.unpack('<q',s.f.read(8))[0]
        if t==13: return struct.unpack('<Q',s.f.read(8))[0]&0x3fffffffffffffff
        if t==14: return struct.unpack('<H',s.f.read(2))[0]
        if t==15: return struct.unpack('<I',s.f.read(4))[0]
        if t==16: return struct.unpack('<Q',s.f.read(8))[0]
        if t==18: return s.lpstr()
        raise ValueError('prim %d'%t)
    def classinfo(s):
        oid=s.i32(); name=s.lpstr(); n=s.i32(); members=[s.lpstr() for _ in range(n)]
        return oid,name,members
    def memtypes(s,n):
        bt=[s.u8() for _ in range(n)]; add=[]
        for b in bt:
            if b in (0,7): add.append(s.u8())
            elif b==3: add.append(s.lpstr())
            elif b==4: add.append((s.lpstr(),s.i32()))
            else: add.append(None)
        return list(zip(bt,add))
    def values(s,obj,members,types):
        for m,(bt,add) in zip(members,types):
            obj[m]= s.prim(add) if bt==0 else s.record()
    def record(s):
        rt=s.u8()
        if rt==0: s.i32(); s.i32(); s.i32(); s.i32(); return 'HEADER'
        if rt==12: lid=s.i32(); s.libs[lid]=s.lpstr(); return s.record()
        if rt in (4,5,2,3):
            oid,name,members=s.classinfo()
            types=s.memtypes(len(members)) if rt in (4,5) else [(None,None)]*len(members)
            if rt==4: types=types  # system class with types
            lib=s.i32() if rt in (5,3) else None
            if rt==2: types=[(1,None) for _ in members]; 
            s.classes[oid]=(name,members,types)
            o=Obj(name,lib); s.objs[oid]=o
            if rt in (2,3):  # untyped: each member is a record
                for m in members: o[m]=s.record()
            else: s.values(o,members,types)
            return o
        if rt==1:
            oid=s.i32(); mid=s.i32(); name,members,types=s.classes[mid]
            o=Obj(name); s.objs[oid]=o; s.values(o,members,types); return o
        if rt==6: oid=s.i32(); v=s.lpstr(); s.objs[oid]=v; return v
        if rt==8: t=s.u8(); return s.prim(t)
        if rt==9: return Ref(s.i32())
        if rt==10: return None
        if rt==13: return NullRun(s.u8())
        if rt==14: return NullRun(s.i32())
        if rt==15:
            oid=s.i32(); n=s.i32(); t=s.u8()
            if t==2: v=s.f.read(n)
            else: v=[s.prim(t) for _ in range(n)]
            s.objs[oid]=v; return v
        if rt in (16,17):
            oid=s.i32(); n=s.i32(); arr=[]
            while len(arr)<n:
                r=s.record()
                if isinstance(r,list) and all(x is None for x in r) and r is not None and False: pass
                if isinstance(r,NullRun): arr.extend([None]*r.n)
                else: arr.append(r)
            # expand null runs
            out=[]
            for r in arr:
                out.append(r)
            s.objs[oid]=out; return out
        if rt==7:
            oid=s.i32(); at=s.u8(); rank=s.i32(); lens=[s.i32() for _ in range(rank)]
            if at in (3,4,5): [s.i32() for _ in range(rank)]
            bt=s.u8(); add=s.memtypes.__func__  # placeholder
            if bt in (0,7): add=s.u8()
            elif bt==3: add=s.lpstr()
            elif bt==4: add=(s.lpstr(),s.i32())
            else: add=None
            total=1
            for l in lens: total*=l
            out=[]
            while len(out)<total:
                if bt==0: out.append(s.prim(add))
                else:
                    r=s.record()
                    if isinstance(r,NullRun): out.extend([None]*r.n)
                    else: out.append(r)
            s.objs[oid]=out; return out
        if rt==11: return 'END'
        raise ValueError('record type %d at %d'%(rt,s.f.tell()))
    def run(s):
        root=None
        while True:
            r=s.record()
            if r=='END': break
            if root is None and isinstance(r,Obj): root=r
        return root
    def resolve(s,v,seen=None):
        if seen is None: seen=set()
        if isinstance(v,Ref):
            if v.id in seen: return f'<cycle {v.id}>'
            return s.resolve(s.objs.get(v.id),seen|{v.id})
        if isinstance(v,Obj):
            o=Obj(v.cls,v.lib)
            for k,x in v.items(): o[k]=s.resolve(x,seen)
            return o
        if isinstance(v,list): return [s.resolve(x,seen) for x in v]
        return v
def load(path):
    r=Reader(open(path,'rb').read()); root=r.run(); return r.resolve(root)
